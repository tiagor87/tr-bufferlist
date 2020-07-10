using System;
using System.Threading;
using System.Threading.Tasks;
using TRBufferList.Core;

namespace ConsoleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            ThreadPool.SetMinThreads(250, 500);

            var source = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var buffer = new BufferList<string>(300, TimeSpan.FromSeconds(5), 3);
            buffer.Cleared += items =>
            {
                Task.Delay(10).GetAwaiter().GetResult();
                Console.WriteLine($"Cleared: {items.Count}\nCount: {buffer.Count}");
            };

            const int size = 200;
            var tasks = new Task[size];

            for (int i = 0; i < size; i++)
            {
                tasks[i] = Task.Factory.StartNew(() =>
                {
                    while (!source.Token.IsCancellationRequested)
                    {
                        buffer.Add("test");
                        Task.Delay(10).Wait();
                    }
                });
            }

            source.Token.Register(buffer.Dispose);

            Task.WaitAll(tasks);

            Console.ReadKey();
        }
    }
}
