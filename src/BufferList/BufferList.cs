using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

namespace BufferList
{
    public delegate void EventHandler<in T>(IReadOnlyList<T> removedItems);
    public delegate Task AsyncEventHandler<in T>(IReadOnlyList<T> removedItems);

    public sealed class BufferList<T> : IEnumerable<T>, IDisposable
    {
        private readonly ConcurrentBag<T> _bag;
        private readonly ConcurrentBag<T> _failedBag;
        private readonly object _sync = new object();
        private readonly Timer _timer;
        private bool _disposed;
        private bool _isCleanningRunning;

        public BufferList(int capacity, TimeSpan clearTtl)
        {
            _timer = new Timer(clearTtl.TotalMilliseconds);
            _timer.Elapsed += TimerOnElapsed;
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
            while (_isCleanningRunning && _bag.Count >= Capacity)
            {
                Task.Delay(10);
            }
            
            _bag.Add(item);

            if (_isCleanningRunning) return;
            if (!IsFull)
            {
                RestartTimer();
                return;
            }
            
            Clear().ConfigureAwait(false);
            
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
            
            try
            {
                var cleanTasks = new List<Task>();
                while (!_bag.IsEmpty)
                {
                    var removed = GetElementsFromBag(_bag);
                    cleanTasks.Add(RaiseEventAsync(removed));
                }

                var failed = GetElementsFromBag(_failedBag);
                cleanTasks.Add(RaiseEventAsync(failed));

                await Task.WhenAll(cleanTasks);
            }
            finally
            {
                _isCleanningRunning = false;
                StartTimer();
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
            Clear().ConfigureAwait(false);
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
            lock (_sync)
            {
                _timer.Start();
            }
        }
        
        private void StopTimer()
        {
            lock (_sync)
            {
                _timer.Stop();
            }
        }
        
        private IReadOnlyList<T> GetElementsFromBag(ConcurrentBag<T> bag)
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