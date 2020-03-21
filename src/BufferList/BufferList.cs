using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

namespace BufferList
{
    public delegate void EventHandler<in T>(IEnumerable<T> removedItems);
    public delegate Task AsyncEventHandler<in T>(IEnumerable<T> removedItems);

    public sealed class BufferList<T> : IEnumerable<T>, IDisposable
    {
        private readonly ConcurrentBag<T> _bag;
        private readonly ConcurrentBag<T> _failedBag;
        private readonly object _sync = new object();
        private readonly Timer _timer;
        private bool _disposed;
        private bool _isProcessing;

        public BufferList(int capacity, TimeSpan clearTtl)
        {
            _timer = new Timer(clearTtl.TotalMilliseconds);
            _timer.Elapsed += TimerOnElapsed;
            _bag = new ConcurrentBag<T>();
            _failedBag = new ConcurrentBag<T>();
            Capacity = capacity;
            _isProcessing = false;
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

        public IEnumerable<T> Failed => _failedBag.ToList();

        public IEnumerator<T> GetEnumerator() => _bag.ToList().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Add(T item)
        {
            RestartTimer();
            _bag.Add(item);
            
            if (!IsFull || _isProcessing) return;
            
            lock (_sync)
            {
                if (_isProcessing) return;
                _isProcessing = true;
            }
            
            Clear();
            
            _isProcessing = false;
        }

        public void Clear()
        {
            if (!_bag.Any()) return;

            while (!_bag.IsEmpty)
            {
                var removed = GetElementsFromBag(_bag);
                RaiseEvent(removed);
            }

            var failed = GetElementsFromBag(_failedBag);
            RaiseEvent(failed);
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
                Clear();
                lock (_sync)
                {
                    _timer?.Dispose();
                }
                Disposed?.Invoke(Failed);
            }

            _disposed = true;
        }

        private void TimerOnElapsed(object sender, ElapsedEventArgs e)
        {
            Clear();
        }

        private void RaiseEvent(IEnumerable<T> removed)
        {
            var removedItems = removed.ToList();
            
            if (!removedItems.Any()) return;
            
            RestartTimer();
            try
            {
                Cleared?.Invoke(removedItems);
                ClearedAsync?.Invoke(removedItems).ConfigureAwait(false);
            }
            catch
            {
                AddRangeToFailed(removedItems);
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
            lock (_sync)
            {
                _timer.Stop();
                _timer.Start();
            }
        }
        
        private IEnumerable<T> GetElementsFromBag(ConcurrentBag<T> bag)
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