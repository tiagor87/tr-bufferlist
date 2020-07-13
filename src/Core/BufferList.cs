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

    public sealed class BufferList<T> : IEnumerable<T>, IDisposable
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

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Get the quantity of items in list.
        /// </summary>
        public int Count => _mainQueue.Count + _faultQueue.Count;

        /// <summary>
        /// Get the capacity of the list.
        /// </summary>
        public int Capacity => _options.MaxSize + _options.MaxFaultSize;

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
        public bool IsOverloaded => _mainQueue.Count >= _options.MaxSize;

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

            Clear().ConfigureAwait(false);
        }

        private void Wait()
        {
            while (!_isDisposing && IsOverloaded)
            {
                _autoResetEvent.Wait(_options.MaxSizeWaitingDelay);
            }
        }

        /// <summary>
        /// Clear the list.
        /// </summary>
        public Task Clear()
        {
            if (!TryStartClearing()) return Task.CompletedTask;
            
            List<T> batch = null;
            try
            {
                // Perform first in an attempt to maintain order
                // and just once to avoid an infinity looping
                if (!_faultQueue.IsEmpty)
                {
                    batch = DequeueBatch(_faultQueue);
                    DispatchEvents(batch);
                }
                
                do
                {
                    batch = DequeueBatch(_mainQueue);
                    DispatchEvents(batch);
                } while (IsReadyToClear);

                return Task.CompletedTask;
            }
            catch
            {
                return Task.CompletedTask;
            }
            finally
            {
                FinishClearing();
                batch?.Clear();
            }
        }

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

        ~BufferList()
        {
            Dispose(false);
        }

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

        private void OnTimerElapsed(object sender)
        {
            Clear().ConfigureAwait(false);
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

        private void RestartTimer()
        {
            StopTimer();
            StartTimer();
        }

        private void StartTimer()
        {
            _timer.Change(_options.IdleClearTtl, Timeout.InfiniteTimeSpan);
        }
        
        private void StopTimer()
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

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

        private void FinishClearing()
        {
            _isClearingRunning = false;
            _autoResetEvent.Set();
            StartTimer();
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
                    Clear().Wait(source.Token);
                }
                catch
                {
                    // ignored
                }
            }
        }
    }
}