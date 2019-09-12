using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Timers;

namespace BufferList
{
    public delegate void EventHandler<in T>(IEnumerable<T> removedItems);

    public sealed class BufferList<T> : IList<T>, IDisposable
    {
        private readonly List<T> _list;
        private readonly Timer _timer;
        private bool _disposed;
        private volatile object _sync = new object();

        public BufferList(int capacity, TimeSpan clearTtl)
        {
            _timer = new Timer(clearTtl.TotalMilliseconds);
            _timer.Elapsed += TimerOnElapsed;
            _list = new List<T>(capacity);
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

        public T this[int index]
        {
            get
            {
                lock (_sync)
                {
                    return _list[index];
                }
            }
            set
            {
                RestartTimer();
                lock (_sync)
                {
                    _list[index] = value;
                }
            }
        }

        public bool IsReadOnly => false;

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
            lock (_sync)
            {
                if (_list.Count == _list.Capacity) Clear();

                _list.Add(item);
            }
        }

        public void Clear()
        {
            List<T> removed;
            lock (_sync)
            {
                if (!_list.Any()) return;
                removed = _list.ToList();
                _list.Clear();
            }

            RaiseEvent(removed);
        }

        public bool Contains(T item)
        {
            lock (_sync)
            {
                return _list.Contains(item);
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (_sync)
            {
                _list.CopyTo(array, arrayIndex);
            }
        }

        public bool Remove(T item)
        {
            RestartTimer();
            lock (_sync)
            {
                var result = _list.Remove(item);
                return result;
            }
        }

        public int IndexOf(T item)
        {
            lock (_sync)
            {
                return _list.IndexOf(item);
            }
        }

        public void Insert(int index, T item)
        {
            RestartTimer();
            lock (_sync)
            {
                _list.Insert(index, item);
            }
        }

        public void RemoveAt(int index)
        {
            RestartTimer();
            lock (_sync)
            {
                _list.RemoveAt(index);
            }
        }

        public event EventHandler<T> Cleared;

        ~BufferList()
        {
            Dispose(false);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing) Clear();

            _disposed = true;
        }

        private void TimerOnElapsed(object sender, ElapsedEventArgs e)
        {
            Clear();
        }

        private void RaiseEvent(IEnumerable<T> removed)
        {
            RestartTimer();
            Cleared?.Invoke(removed);
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