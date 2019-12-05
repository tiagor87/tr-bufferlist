using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace BufferList.UnitTests
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
            for (var i = 0; i < 1001; i++) list.Add(i);
            
            autoResetEvent.WaitOne();

            removedCount.Should().Be(1000);
            list.Should().HaveCount(1);
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
                removedCount = removed.Count();
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
            list.Failed.Should().NotBeEmpty();
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
            list.Failed.Should().HaveCount(1);
        }

        [Fact]
        public async Task GivenBufferShouldTryToCleanListUntilBagIsEmpty()
        {
            var readded = 0;
            var maxSize = 0;
            var list = new BufferList<int>(10, TimeSpan.FromSeconds(10));
            list.Cleared += removed =>
            {
                ++readded;
                if (readded >= 100) return;
                maxSize = Math.Max(maxSize, removed.Count());

                foreach (var remove in removed)
                {
                    list.Add(remove);
                }
            };

            var tasks = new List<Task>();
            for (var i = 0; i < 10000; i++)
            {
                tasks.Add(Task.Run(() => list.Add(i)));
            }

            await Task.WhenAll(tasks);
            Thread.Sleep(100);
            list.Count.Should().Be(0);
            maxSize.Should().Be(10);
            list.Dispose();
        }
    }
}