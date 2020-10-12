using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace ConsoleApp
{
    public class Program
    {
        private static BufferList.BufferList<int> _oldBufferList;
        private static TRBufferList.Core.BufferList<int> _newBufferList;

        static void Main(string[] args)
        {
            ThreadPool.SetMinThreads(250, 250);
            BenchmarkRunner.Run<Program>(DefaultConfig.Instance.WithOption(ConfigOptions.DisableOptimizationsValidator, true));
        }

        [GlobalSetup]
        public void Setup()
        {
            static void OnCleared(IEnumerable<int> removedItems)
            {
                Task.Delay(250).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            _oldBufferList = new BufferList.BufferList<int>(100, TimeSpan.FromSeconds(5));
            _oldBufferList.Cleared += OnCleared;
            _newBufferList = new TRBufferList.Core.BufferList<int>(100, TimeSpan.FromSeconds(5));
            _newBufferList.Cleared += OnCleared;
        }

        [IterationCleanup]
        public void IterationCleanUp()
        {
            _newBufferList.Clear();
            _oldBufferList.Clear();
        }

        [GlobalCleanup]
        public void GlobalCleanUp()
        {
            _newBufferList.Dispose();
            _oldBufferList.Dispose();
        }
        
        [Benchmark]
        public void NewBufferList()
        {
            for (var i = 0; i < 500; i++)
            {
                _newBufferList.Add(i);
            }
        }

        [Benchmark]
        public void OldBufferList()
        {
            for (var i = 0; i < 500; i++)
            {
                _oldBufferList.Add(i);
            }
        }
    }
}
