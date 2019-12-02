using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using Timer = System.Timers.Timer;

namespace BufferList
{
    public delegate void EventHandler<in T>(IEnumerable<T> removedItems);

    public sealed class BufferList<T> : IEnumerable<T>, IDisposable
    {
        private readonly ConcurrentBag<T> _list;
        private readonly ConcurrentBag<T> _failedList;
        private readonly object _sync = new object();
        private readonly Timer _timer;
        private bool _disposed;
        private readonly int _capacity;

        public BufferList(int capacity, TimeSpan clearTtl)
        {
            _timer = new Timer(clearTtl.TotalMilliseconds);
            _timer.Elapsed += TimerOnElapsed;
            _list = new ConcurrentBag<T>();
            _failedList = new ConcurrentBag<T>();
            _capacity = capacity;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _list.Count;
                }
            }
        }
        
        public int Capacity => _capacity;

        public bool IsReadOnly => false;
        
        public bool IsFull => Count >= Capacity;

        public IEnumerable<T> Failed => _failedList.ToList();

        public IEnumerator<T> GetEnumerator()
        {
            lock (_sync)
            {
                return _list.ToList().GetEnumerator();
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Add(T item)
        {
            RestartTimer();
            _list.Add(item);
            if (IsFull) Clear();
        }

        public void Clear()
        {
            if (!_list.Any()) return;
            
            var removed = new List<T>();
            while (!_list.IsEmpty && removed.Count < Capacity)
            {
                if (_list.TryTake(out var item)) removed.Add(item);
            }

            while (!_failedList.IsEmpty)
            {
                if (_failedList.TryTake(out var item)) removed.Add(item);
            } 

            RaiseEvent(removed);
        }

        public event EventHandler<T> Cleared;
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
            RestartTimer();
            var removedItems = removed.ToList();
            try
            {
                Cleared?.Invoke(removedItems);
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
                _failedList.Add(item);
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
    }
}