using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TRBufferList.Core
{
    public delegate void EventHandler<in T>(IReadOnlyList<T> removedItems);

    public sealed class BufferList<T> : ICollection<T>, IDisposable
    {
        private readonly BufferListOptions _options;
        private readonly ConcurrentQueue<T> _mainQueue;
        private readonly ConcurrentQueue<T> _faultQueue;
        private readonly object _sync = new object();
        private readonly Timer _timer;
        private readonly ManualResetEventSlim _autoResetEvent;
        private bool _disposed;
        private bool _isDisposing;
        private bool _isClearingRunning;

        /// <summary>
        /// Initialize a new instance of BufferList.
        /// </summary>
        /// <param name="batchingSize">Clear batching size.</param>
        /// <param name="clearTtl">Time to clean list when idle.</param>
        public BufferList(int batchingSize, TimeSpan clearTtl) : this(BufferListOptions.Simple(batchingSize, clearTtl))
        {
        }

        public BufferList(BufferListOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _autoResetEvent = new ManualResetEventSlim(false);
            _timer = new Timer(OnTimerElapsed, null, _options.IdleClearTtl, Timeout.InfiniteTimeSpan);
            _mainQueue = new ConcurrentQueue<T>();
            _faultQueue = new ConcurrentQueue<T>();
            _isClearingRunning = false;
        }

        public bool Remove(T item)
        {
            return false;
        }

        /// <summary>
        /// Get the quantity of items in list.
        /// </summary>
        public int Count => _mainQueue.Count + _faultQueue.Count;

        public bool IsReadOnly => false;
        
        /// <summary>
        /// Checks if buffer size is greater than batching size.
        /// </summary>
        public bool IsReadyToClear => _mainQueue.Count >= _options.ClearBatchingSize;

        /// <summary>
        /// Checks if fault queue size is greater than max fault size.
        /// </summary>
        public bool IsFaultListFull => _faultQueue.Count >= _options.MaxFaultSize;

        /// <summary>
        /// Checks if buffer size is greater than max size.
        /// </summary>
        public bool IsOverloaded => _options.MaxSize.HasValue && _mainQueue.Count >= _options.MaxSize;
        
        /// <summary>
        /// Event is called everytime the list is cleared.
        /// </summary>
        public event EventHandler<T> Cleared;
        
        /// <summary>
        /// Event is called just before the list is disposed.
        /// </summary>
        public event EventHandler<T> Disposed;

        /// <summary>
        /// Event is called when failure queue is full and some drop happens.
        /// </summary>
        public event EventHandler<T> Dropped;

        /// <summary>
        /// Get items that failed to publish.
        /// </summary>
        /// <returns>List of items.</returns>
        public IReadOnlyList<T> GetFailed() => _faultQueue.ToList();

        public IEnumerator<T> GetEnumerator() => _mainQueue.ToList().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Add new item to list.
        ///
        /// When capacity is achievied, <see cref="Clear"/> is executed.
        /// When block capacity is achievied, Add will wait to finish execution.
        /// </summary>
        /// <param name="item">The item to add.</param>
        public void Add(T item)
        {
            if (_isDisposing) throw new InvalidOperationException("The buffer has been disposed.");

            Wait();
            
            _mainQueue.Enqueue(item);

            if (_isClearingRunning) return;
            if (!IsReadyToClear)
            {
                RestartTimer();
                return;
            }

            Task.Factory.StartNew(Clear)
                .ConfigureAwait(false);
        }

        public bool Contains(T item)
        {
            return _mainQueue.Contains(item) || _faultQueue.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            
        }

        /// <summary>
        /// Clear the list.
        /// </summary>
        public void Clear()
        {
            if (!TryStartClearing()) return;
            
            List<T> batch = null;
            try
            {
                // Perform first in an attempt to maintain order
                // and just once to avoid an infinity looping
                if (!_faultQueue.IsEmpty)
                {
                    try
                    {
                        batch = DequeueBatch(_faultQueue);
                        DispatchEvents(batch);
                    }
                    finally
                    {
                        batch?.Clear();
                    }
                }
                
                do
                {
                    try
                    {
                        batch = DequeueBatch(_mainQueue);
                        DispatchEvents(batch);
                    }
                    finally
                    {
                        batch?.Clear();
                        if (_options.GcCollectionOptions.HasFlag(BufferListGcCollectionOptions.EachBatchingClearExecution))
                        {
                            GC.Collect();
                        }
                    }
                } while (IsReadyToClear);
            }
            catch
            {
                // ignored
            }
            finally
            {
                FinishClearing();
            }
        }
        
        /// <summary>
        /// Performs application-defined tasks associated with freeing,
        /// releasing, or resetting unmanaged resources. 
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Finalize instance.
        /// </summary>
        ~BufferList()
        {
            Dispose(false);
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing,
        /// releasing, or resetting unmanaged resources. 
        /// </summary>
        /// <param name="disposing">It's <value>true</value> when user called <see cref="Dispose"/>.</param>
        private void Dispose(bool disposing)
        {
            if (_disposed) return;
            
            if (disposing)
            {
                _isDisposing = true;
                CleanUp(_options.DisposeTimeout);
                Disposed?.Invoke(GetFailed());
                _timer.Dispose();
            }

            _disposed = true;
        }

        /// <summary>
        /// When timer elapsed, try to clear.
        /// </summary>
        /// <param name="sender"></param>
        private void OnTimerElapsed(object sender)
        {
            Task.Factory.StartNew(() => Clear())
                    .ConfigureAwait(false);
        }

        /// <summary>
        /// Execute clear event for items removed from bag.
        /// </summary>
        /// <param name="items"></param>
        private void DispatchEvents(IReadOnlyList<T> items)
        {
            if (!items.Any()) return;

            try
            {
                Cleared?.Invoke(items);
            }
            catch
            {
                EnqueueFailures(items);
            }
        }
        
        /// <summary>
        /// Enqueue items to failure queue.
        /// </summary>
        /// <param name="items"></param>
        private void EnqueueFailures(IReadOnlyList<T> items)
        {
            var dropped = new List<T>(items.Count);
            foreach (var item in items)
            {
                if (IsFaultListFull && _faultQueue.TryDequeue(out var drop))
                {
                    dropped.Add(drop);
                }
                _faultQueue.Enqueue(item);
            }
            
            if (dropped.Any()) Dropped?.Invoke(dropped);
        }

        /// <summary>
        /// Stop and start timer.
        /// </summary>
        private void RestartTimer()
        {
            StopTimer();
            StartTimer();
        }

        /// <summary>
        /// Start timer.
        /// </summary>
        private void StartTimer()
        {
            _timer.Change(_options.IdleClearTtl, Timeout.InfiniteTimeSpan);
        }
        
        /// <summary>
        /// Stop timer.
        /// </summary>
        private void StopTimer()
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Try to set flag to start clearing, checks if clear is still necessary
        /// and stop timer.
        /// </summary>
        /// <returns></returns>
        private bool TryStartClearing()
        {
            if (_isClearingRunning || _mainQueue.IsEmpty && _faultQueue.IsEmpty) return false;

            lock (_sync)
            {
                if (_isClearingRunning) return false;
                _isClearingRunning = true;
            }
            
            StopTimer();
            return true;
        }

        /// <summary>
        /// Reset clearing flag and restart time.
        /// </summary>
        private void FinishClearing()
        {
            if (_options.GcCollectionOptions.HasFlag(BufferListGcCollectionOptions.EachFullClearExecution))
            {
                GC.Collect();
            }

            _isClearingRunning = false;
            _autoResetEvent.Set();
            StartTimer();
        }
        
        /// <summary>
        /// Wait for list to not be overloaded.
        ///
        /// When list is disposed, it will allow all awaiting adds to execute.
        /// </summary>
        private void Wait()
        {
            while (!_isDisposing && IsOverloaded)
            {
                _autoResetEvent.Wait(_options.MaxSizeWaitingDelay);
            }
        }
        
        /// <summary>
        /// Get items from bag with exactly capacity quantity.
        /// </summary>
        /// <param name="queue"></param>
        /// <returns>List of items.</returns>
        private List<T> DequeueBatch(ConcurrentQueue<T> queue)
        {
            var list = new List<T>(Math.Min(_options.ClearBatchingSize, queue.Count));
            while (!queue.IsEmpty && list.Count < _options.ClearBatchingSize)
            {
                if (queue.TryDequeue(out var item)) list.Add(item);
            }
            return list;
        }

        /// <summary>
        /// Wait for all add tasks to finish, then execute clear until bag and failed bad are empties.
        /// </summary>
        /// <param name="timeout"></param>
        private void CleanUp(TimeSpan timeout)
        {
            var source = new CancellationTokenSource(timeout);
            while (!source.IsCancellationRequested && (!_mainQueue.IsEmpty || !_faultQueue.IsEmpty))
            {
                try
                {
                    Clear();
                }
                catch
                {
                    // ignored
                }
            }
        }
    }
}