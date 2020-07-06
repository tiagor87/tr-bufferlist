using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TRBufferList.Core
{
    public delegate void EventHandler<in T>(IReadOnlyList<T> removedItems);

    public sealed class BufferList<T> : IEnumerable<T>, IDisposable
    {
        private readonly TimeSpan _clearTtl;
        private readonly ConcurrentBag<T> _bag;
        private readonly ConcurrentBag<T> _failedBag;
        private readonly object _sync = new object();
        private readonly Timer _timer;
        private readonly AutoResetEvent _autoResetEvent;
        private bool _disposed;
        private bool _isCleanningRunning;

        /// <summary>
        /// Initialize a new instance of BufferList.
        /// </summary>
        /// <param name="capacity">Limit of items.</param>
        /// <param name="clearTtl">Time to clean list when idle.</param>
        /// <param name="blockCapacity">Limit to block new items.</param>
        public BufferList(int capacity, TimeSpan clearTtl, int? blockCapacity = null)
        {
            _autoResetEvent = new AutoResetEvent(false);
            _clearTtl = clearTtl;
            _timer = new Timer(TimerOnElapsed, null, clearTtl, Timeout.InfiniteTimeSpan);
            _bag = new ConcurrentBag<T>();
            _failedBag = new ConcurrentBag<T>();
            Capacity = capacity;
            BlockCapacity = blockCapacity ?? capacity * 2;
            _isCleanningRunning = false;
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
            if (_isCleanningRunning || _bag.Count >= BlockCapacity)
            {
                _autoResetEvent.WaitOne();
            }
            
            _bag.Add(item);

            if (!IsFull)
            {
                RestartTimer();
                return;
            }
            
            Clear();
        }

        /// <summary>
        /// Clear the list.
        /// </summary>
        public void Clear()
        {
            if (_bag.IsEmpty && _failedBag.IsEmpty || _isCleanningRunning) return;

            lock (_sync)
            {
                if (_isCleanningRunning) return;
                _isCleanningRunning = true;
            }

            StopTimer();

            List<T> removed = null;
            try
            {
                while (!_bag.IsEmpty)
                {
                    removed = GetElementsFromBag(_bag);
                    DispatchEvents(removed);
                }

                if (_failedBag.IsEmpty) return;
                
                removed = GetElementsFromBag(_failedBag);
                DispatchEvents(removed);
            }
            finally
            {
                removed?.Clear();
                StartTimer();
                _isCleanningRunning = false;
                _autoResetEvent.Set();
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

            if (disposing)
            {
                Clear();
                _timer.Dispose();
                Disposed?.Invoke(GetFailed().ToList());
            }

            _disposed = true;
        }

        private void TimerOnElapsed(object sender)
        {
            Clear();
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
    }
}