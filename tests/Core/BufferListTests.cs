using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace TRBufferList.Core.Tests
{
    public class BufferListTests
    {
        [Fact]
        public void GivenBufferListWhenCapacityAchievedShouldClear()
        {
            var removedCount = 0;

            var list = new BufferList<int>(100, Timeout.InfiniteTimeSpan);
            var autoResetEvent = new AutoResetEvent(false);
            list.Cleared += removed =>
            {
                removedCount = removed.Count;
                autoResetEvent.Set();
            };
            for (var i = 0; i <= 100; i++) list.Add(i);
            
            autoResetEvent.WaitOne();
            removedCount.Should().Be(100);
            list.Should().HaveCount(1);
        }
        
        [Fact]
        public async Task GivenBufferListWhenClearShouldDispatchAListCopy()
        {
            IReadOnlyList<int> removedList = null;

            var list = new BufferList<int>(1000, Timeout.InfiniteTimeSpan);
            var autoResetEvent = new AutoResetEvent(false);
            list.Cleared += removed =>
            {
                removedList = removed;
                autoResetEvent.Set();
            };
            for (var i = 0; i <= 1000; i++) list.Add(i);
            
            autoResetEvent.WaitOne();
            
            GC.Collect(0);
            GC.Collect(1);
            GC.Collect(2);
            await Task.Delay(TimeSpan.FromSeconds(5));

            removedList.Should().NotBeNull();
        }

        [Fact]
        public void GivenBufferListWhenContainsCalledShouldVerifyItem()
        {
            var list = new BufferList<int>(1000, TimeSpan.FromSeconds(10))
            {
                1
            };

            list.Contains(1).Should().BeTrue();
        }

        [Fact]
        public void GivenBufferListWhenDisposeShouldClear()
        {
            var removedCount = 0;

            var list = new BufferList<int>(1000, TimeSpan.FromSeconds(10));
            list.Cleared += removed => { removedCount = removed.Count(); };
            for (var i = 0; i < 1000; i++) list.Add(i);

            list.Dispose();
            removedCount.Should().Be(1000);
        }

        [Fact]
        public void GivenBufferListWhenDisposeTwiceShouldNotThrow()
        {
            var list = new BufferList<int>(1000, TimeSpan.FromSeconds(10));

            list.Dispose();
            list.Invoking(x => x.Dispose()).Should().NotThrow();
        }

        [Fact]
        public void GivenBufferListWhenEmptyEventsShouldNotThrow()
        {
            var list = new BufferList<int>(1000, TimeSpan.FromSeconds(10))
            {
                1
            };

            list.Invoking(x => x.Clear()).Should().NotThrow();
            list.Should().BeEmpty();
        }

        [Fact]
        public void GivenBufferListWhenEmptyShouldNotDispatchCleared()
        {
            var list = new BufferList<int>(1000, TimeSpan.FromSeconds(10));
            var called = false;
            list.Cleared += _ => called = true;

            list.Clear();

            called.Should().BeFalse();
        }

        [Fact]
        public void GivenBufferListWhenInstantiateShouldBeEmptyAndNotReadOnly()
        {
            var list = new BufferList<int>(1000, TimeSpan.FromSeconds(10));

            list.Should().NotBeNull();
            list.Should().BeEmpty();
            list.IsReadOnly.Should().BeFalse();
        }

        [Fact]
        public void GivenBufferListWhenTtlElapsedShouldClear()
        {
            var autoResetEvent = new AutoResetEvent(false);
            var removedCount = 0;

            var list = new BufferList<int>(1000, TimeSpan.FromSeconds(1));
            list.Cleared += removed =>
            {
                removedCount = removed.Count;
                autoResetEvent.Set();
            };
            for (var i = 0; i < 999; i++) list.Add(i);
            autoResetEvent.WaitOne();

            removedCount.Should().Be(999);
            list.Should().BeEmpty();
        }

        [Fact]
        public void GivenBufferWhenThrowOnClearingShouldRequeue()
        {
            const int BATCHING_SIZE = 100;
            var list = new BufferList<int>(BATCHING_SIZE, TimeSpan.FromSeconds(1));
            var faultCount = 0;
            list.Cleared += removed => throw new Exception();
            list.Disposed += failed => Interlocked.Add(ref faultCount, failed.Count);
            list.Dropped += dropped => Interlocked.Add(ref faultCount, dropped.Count);

            for (var i = 0; i < 1000; i++)
            {
                list.Add(i);
            }
            
            list.Dispose();
            faultCount.Should().Be(1000);
        }

        [Fact]
        public async Task GivenBufferWhenFailedHasAnyShouldDispatchClearForFailedMessages()
        {
            var list = new BufferList<int>(1, Timeout.InfiniteTimeSpan);
            var autoResetEvent = new AutoResetEvent(false);
            var i = 0;
            list.Cleared += removed =>
            {
                autoResetEvent.Set();
                if (++i == 1) throw new Exception();
            };

            
            list.Add(1);
            autoResetEvent.WaitOne();
            await Task.Delay(300);
            list.GetFailed().Should().NotBeEmpty();
            list.Clear();
            list.GetFailed().Should().BeEmpty();
        }

        [Fact]
        public void GivenBufferShouldTryToCleanListUntilBagIsEmpty()
        {
            var read = 0;
            var maxSize = 0;
            var count = 0;
            var list = new BufferList<int>(10, Timeout.InfiniteTimeSpan);
            var autoResetEvent = new AutoResetEvent(false);
            list.Cleared += removed =>
            {
                maxSize = Math.Max(maxSize, removed.Count);
                count += removed.Count;
                ++read;
                if (read >= 10)
                {
                    autoResetEvent.Set();
                };
            };

            for (var i = 0; i < 100; i++)
            {
                list.Add(i);
            }

            autoResetEvent.WaitOne();
            maxSize.Should().Be(10);
            count.Should().BeCloseTo(100, 10);
            list.Dispose();
        }

        [Fact]
        public void GivenBufferShouldCountFailedList()
        {
            var list = new BufferList<int>(10, Timeout.InfiniteTimeSpan)
            {
                1
            };
            list.Cleared += items => throw new Exception();
            
            list.Clear();
            
            list.Count.Should().Be(1);
        }

        [Fact]
        public async Task GivenBufferListWhenDisposeShouldFullClearList()
        {
            var count = 0;
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var list = new BufferList<int>(10, Timeout.InfiniteTimeSpan);
            list.Cleared += removed => Interlocked.Add(ref count, removed.Count);
            var tasks = new Task[10];
            var expected = 0;
            for (var i = 0; i < 10; i++)
            {
                tasks[i] = Task.Factory.StartNew(async () =>
                {
                    while (!source.Token.IsCancellationRequested)
                    {
                        list.Add(i);
                        Interlocked.Increment(ref expected);
                        await Task.Delay(10);
                    }
                });
            }
            
            var autoResetEvent = new AutoResetEvent(false);
            source.Token.Register(() =>
            {
                list.Dispose();
                autoResetEvent.Set();
            });

            autoResetEvent.WaitOne();
            await Task.WhenAll(tasks);

            count.Should().Be(expected);
        }

        [Fact]
        public async Task GivenBufferListWhenFaultListFullShouldDrop()
        {
            var list = new BufferList<int>(new BufferListOptions(
                1,
                1,
                1,
                Timeout.InfiniteTimeSpan,
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(100)));

            var dropped = new List<int>(1);
            var autoResetEvent = new AutoResetEvent(false);
            var i = 0;
            list.Cleared += _ =>
            {
                if (++i == 2) autoResetEvent.Set();
                throw new Exception();
            };
            list.Dropped += items => dropped.AddRange(items);

            list.Add(1);
            list.Add(2);
            await Task.Delay(250);
            dropped.Should().HaveCount(1);
            dropped.First().Should().Be(1);
            list.GetFailed().Should().HaveCount(1);
            list.GetFailed().First().Should().Be(2);
        }

        [Fact]
        public void GivenBufferListWhenAddAndDisposedShouldThrow()
        {
            var list = new BufferList<int>(1, Timeout.InfiniteTimeSpan);
            list.Dispose();

            Action action = () => list.Add(1);

            action.Should().Throw<InvalidOperationException>()
                .WithMessage("The buffer has been disposed.");
        }
    }
}