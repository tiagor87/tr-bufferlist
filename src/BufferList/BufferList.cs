using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BufferList
{
    public delegate void EventHandler<in T>(IReadOnlyList<T> removedItems);
    public delegate Task AsyncEventHandler<in T>(IReadOnlyList<T> removedItems);

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

        public BufferList(int capacity, TimeSpan clearTtl)
        {
            _autoResetEvent = new AutoResetEvent(false);
            _clearTtl = clearTtl;
            _timer = new Timer(TimerOnElapsed, null, clearTtl, Timeout.InfiniteTimeSpan);
            _bag = new ConcurrentBag<T>();
            _failedBag = new ConcurrentBag<T>();
            Capacity = capacity;
            _isCleanningRunning = false;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public int Count => _bag.Count;

        public int Capacity { get; }

        public bool IsReadOnly => false;
        
        public bool IsFull => Count >= Capacity;

        public IReadOnlyList<T> Failed => _failedBag.ToList();

        public IEnumerator<T> GetEnumerator() => _bag.ToList().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Add(T item)
        {
            if (_isCleanningRunning || _bag.Count >= Capacity)
            {
                _autoResetEvent.WaitOne();
            }
            
            _bag.Add(item);

            if (_isCleanningRunning) return;
            if (!IsFull)
            {
                RestartTimer();
                return;
            }
            
            Clear().GetAwaiter().GetResult();
            
        }

        public async Task Clear()
        {
            if (!_bag.Any() || _isCleanningRunning) return;

            lock (_sync)
            {
                if (_isCleanningRunning) return;
                _isCleanningRunning = true;
            }

            StopTimer();

            List<T> removed = null;
            List<T> failed = null;
            try
            {
                var cleanTasks = new List<Task>();
                while (!_bag.IsEmpty)
                {
                    removed = GetElementsFromBag(_bag);
                    cleanTasks.Add(RaiseEventAsync(removed));
                }

                failed = GetElementsFromBag(_failedBag);
                cleanTasks.Add(RaiseEventAsync(failed));

                await Task.WhenAll(cleanTasks);
            }
            finally
            {
                removed?.Clear();
                failed?.Clear();
                StartTimer();
                _isCleanningRunning = false;
                _autoResetEvent.Set();
            }
        }

        public event EventHandler<T> Cleared;
        public event AsyncEventHandler<T> ClearedAsync;
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
                Clear().GetAwaiter().GetResult();
                _timer.Dispose();
                Disposed?.Invoke(Failed);
            }

            _disposed = true;
        }

        private void TimerOnElapsed(object sender)
        {
            Clear().GetAwaiter().GetResult();
        }

        private async Task RaiseEventAsync(IReadOnlyList<T> removed)
        {
            if (!removed.Any()) return;

            try
            {
                if (ClearedAsync != null)
                {
                    await ClearedAsync(removed);
                    return;
                }

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