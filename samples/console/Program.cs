using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace BufferList.Samples.Console
{
    class Program
    {
        private static BufferList<Event> _buffer;
        
        static void Main(string[] args)
        {
            _buffer = new BufferList<Event>(5000, TimeSpan.FromSeconds(10));

            _buffer.ClearedAsync += items =>
            {
                Thread.Sleep(250);
                return Task.CompletedTask;
            };
            
            var tasks = new List<Task>();
            
            var cancellationSource = new CancellationTokenSource();

            for (var i = 0; i < 10; i++)
            {
                tasks.Add(Task.Factory.StartNew(() =>
                {
                    while (!cancellationSource.IsCancellationRequested)
                    {
                        _buffer.Add(new Event());
                    }
                }, cancellationSource.Token));
            }

            Task.WaitAll(tasks.ToArray(), TimeSpan.FromMinutes(1));
            
            cancellationSource.Cancel();
            
            Thread.Sleep(1000);
            
            tasks.Clear();
            
            _buffer.Dispose();

            System.Console.ReadLine();
        }
    }

    class Event
    {
        public Event()
        {
            CreatedAt = DateTime.UtcNow;
            Text = Guid.NewGuid().ToString();
        }
        public DateTime CreatedAt { get; set; }
        public string Text { get; set; }
    }
}
