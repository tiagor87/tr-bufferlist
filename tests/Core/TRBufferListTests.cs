using System;
using System.Collections.Generic;
using System.Linq;
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

            var list = new BufferList<int>(1000, TimeSpan.FromSeconds(5));
            var autoResetEvent = new AutoResetEvent(false);
            list.Cleared += removed =>
            {
                removedCount = removed.Count();
                autoResetEvent.Set();
            };
            for (var i = 0; i <= 1000; i++) list.Add(i);
            
            autoResetEvent.WaitOne();

            removedCount.Should().Be(1000);
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
            var list = new BufferList<int>(100, TimeSpan.FromSeconds(1));
            list.Cleared += removed => throw new Exception();
            list.Disposed += failed => failed.Should().HaveCount(1000);
            for (var i = 0; i < 1000; i++) list.Add(i);
            list.Capacity.Should().Be(100);
            list.GetFailed().Should().NotBeEmpty();
            list.Dispose();
        }

        [Fact]
        public void GivenBufferWhenFailedHasAnyShouldDispatchClearForFailedMessages()
        {
            var list = new BufferList<int>(1, TimeSpan.FromSeconds(10));
            var dispatched = 0;
            list.Cleared += removed =>
            {
                ++dispatched;
                throw new Exception();
            };

            list.Add(1);
            dispatched.Should().Be(2);
            list.GetFailed().Should().HaveCount(1);
        }

        [Fact]
        public void GivenBufferShouldTryToCleanListUntilBagIsEmpty()
        {
            var read = 0;
            var maxSize = 0;
            var count = 0;
            var list = new BufferList<int>(10, Timeout.InfiniteTimeSpan);
            list.Cleared += removed =>
            {
                count += removed.Count;
                ++read;
                if (read >= 100) return;
                maxSize = Math.Max(maxSize, removed.Count());
            };

            for (var i = 0; i < 1000; i++)
            {
                list.Add(i);
            }
            maxSize.Should().Be(10);
            count.Should().Be(1000);
            list.Dispose();
        }

        [Fact]
        public void GivenBufferShouldWaitToAddWhenFull()
        {
            var waitTime = TimeSpan.FromSeconds(1);
            const int capacity = 10;
            var list = new BufferList<int>(capacity, Timeout.InfiniteTimeSpan, 1);
            for (var i = 1; i < capacity; i++)
            {
                list.Add(i);
            }

            list.Cleared += items => Task.Delay(waitTime).Wait();
            var task = Task.WhenAny(Task.Factory.StartNew(() => list.Add(10)),
                Task.Factory.StartNew(() => list.Add(11)));
            task.ExecutionTimeOf(x => x.Wait())
                .Should()
                .BeGreaterOrEqualTo(waitTime);
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
        public void GivenBufferWhenBlockCapacityIsInvalidShouldThrow()
        {
            Action action = () => new BufferList<int>(10, Timeout.InfiniteTimeSpan, 0);

            action.Should().Throw<ArgumentException>();
        }
    }
}