using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TRBufferList.Core
{
    public delegate void EventHandler<in T>(IReadOnlyList<T> removedItems);

    public sealed class BufferList<T> : IEnumerable<T>, IDisposable
    {
        private const int BLOCK_CAPACITY_DEFAULT_MULTIPLIER = 2;
        private readonly TimeSpan _clearTtl;
        private readonly ConcurrentBag<T> _bag;
        private readonly ConcurrentBag<T> _failedBag;
        private readonly object _sync = new object();
        private readonly Timer _timer;
        private readonly ManualResetEventSlim _autoResetEvent;
        private bool _disposed;
        private bool _isDisposing;
        private bool _isCleanningRunning;
        private ConcurrentDictionary<int, Task> _addTasks;

        /// <summary>
        /// Initialize a new instance of BufferList.
        /// </summary>
        /// <param name="capacity">Limit of items.</param>
        /// <param name="clearTtl">Time to clean list when idle.</param>
        /// <param name="blockCapacityMultiplier">Multiplier applied over capacity to block new items.</param>
        public BufferList(int capacity, TimeSpan clearTtl, int blockCapacityMultiplier = BLOCK_CAPACITY_DEFAULT_MULTIPLIER)
        {
            if (blockCapacityMultiplier < 1)
                throw new ArgumentException("The value should be greater than 1.", nameof(blockCapacityMultiplier));
            _autoResetEvent = new ManualResetEventSlim(false);
            _clearTtl = clearTtl;
            _timer = new Timer(TimerOnElapsed, null, clearTtl, Timeout.InfiniteTimeSpan);
            _bag = new ConcurrentBag<T>();
            _failedBag = new ConcurrentBag<T>();
            Capacity = capacity;
            BlockCapacity = capacity * blockCapacityMultiplier;
            _isCleanningRunning = false;
            _addTasks = new ConcurrentDictionary<int, Task>();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Get the quantity of items in list.
        /// </summary>
        public int Count => _bag.Count + _failedBag.Count;

        /// <summary>
        /// Get the limit of items to start cleanning.
        /// </summary>
        public int Capacity { get; }
        
        /// <summary>
        /// Get the limit to block add new items.
        /// </summary>
        public int BlockCapacity { get; }

        public bool IsReadOnly => false;
        
        /// <summary>
        /// Get the status if buffer is full.
        /// </summary>
        public bool IsFull => _bag.Count >= Capacity;

        /// <summary>
        /// Get items that failed to publish.
        /// </summary>
        /// <returns>List of items.</returns>
        public IReadOnlyList<T> GetFailed() => _failedBag.ToList();

        public IEnumerator<T> GetEnumerator() => _bag.ToList().GetEnumerator();

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
            
            var task = Task.Factory.StartNew(async () =>
            {
                while (!_isDisposing && _bag.Count >= BlockCapacity)
                {
                    _autoResetEvent.Wait(TimeSpan.FromMilliseconds(200));
                }
                
                _bag.Add(item);

                if (_isCleanningRunning || !IsFull)
                {
                    RestartTimer();
                    return;
                }
            
                await Clear();
            });
            _addTasks.TryAdd(task.Id, task);
            task.ContinueWith(t => _addTasks.TryRemove(t.Id, out _));
        }

        /// <summary>
        /// Clear the list.
        /// </summary>
        public Task Clear()
        {
            if (_bag.IsEmpty && _failedBag.IsEmpty || _isCleanningRunning) return Task.CompletedTask;

            lock (_sync)
            {
                if (_isCleanningRunning) return Task.CompletedTask;
                _isCleanningRunning = true;
            }

            StopTimer();

            var stopwatch = new Stopwatch();
            stopwatch.Start();
            List<T> removed = null;
            try
            {
                do
                {
                    removed = GetElementsFromBag(_bag);
                    DispatchEvents(removed);
                } while (_bag.Count >= Capacity);

                if (_failedBag.IsEmpty) return Task.CompletedTask;
                
                removed = GetElementsFromBag(_failedBag);
                DispatchEvents(removed);
                return Task.CompletedTask;
            }
            finally
            {
                _isCleanningRunning = false;
                _autoResetEvent.Set();
                StartTimer();
                removed?.Clear();
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

        ~BufferList()
        {
            Dispose(false);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;
            
            _isDisposing = true;
            if (disposing)
            {
                FullClear(TimeSpan.FromSeconds(10));
                _timer.Dispose();
                Disposed?.Invoke(GetFailed().ToList());
            }

            _disposed = true;
        }

        private void TimerOnElapsed(object sender)
        {
            Clear().ConfigureAwait(false);
        }

        private void DispatchEvents(IReadOnlyList<T> removed)
        {
            if (!removed.Any()) return;

            try
            {
                Cleared?.Invoke(removed);
            }
            catch
            {
                AddRangeToFailed(removed);
            }
        }
        
        private void AddRangeToFailed(IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                _failedBag.Add(item);
            }
        }

        private void RestartTimer()
        {
            StopTimer();
            StartTimer();
        }

        private void StartTimer()
        {
            _timer.Change(_clearTtl, Timeout.InfiniteTimeSpan);
        }
        
        private void StopTimer()
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        
        private List<T> GetElementsFromBag(ConcurrentBag<T> bag)
        {
            var list = new List<T>();
            while (!bag.IsEmpty && list.Count < Capacity)
            {
                if (bag.TryTake(out var item)) list.Add(item);
            }
            return list;
        }

        private void FullClear(TimeSpan timeout)
        {
            var source = new CancellationTokenSource(timeout);
            Task.WaitAll(_addTasks.Values.ToArray(), source.Token);
            while (!source.IsCancellationRequested && (!_bag.IsEmpty || !_failedBag.IsEmpty))
            {
                Clear().Wait(source.Token);
            }
        }
    }
}