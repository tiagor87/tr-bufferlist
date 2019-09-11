using System;
using System.Linq;
using System.Threading;
using BufferList.Core;
using FluentAssertions;
using Xunit;

namespace BufferList.UnitTests
{
    public class BufferListTests
    {
        [Fact]
        public void GivenBufferListWhenTtlElapsedShouldClear()
        {
            var autoResetEvent = new AutoResetEvent(false);
            var removedCount = 0;
            
            var list = new BufferList<int>(1000, TimeSpan.FromSeconds(1));
            list.Cleared += (removed) =>
            {
                removedCount = removed.Count();
                autoResetEvent.Set();
            };
            for (var i = 0; i < 999; i++)
            {
                list.Add(i);
            }
            autoResetEvent.WaitOne();
            
            removedCount.Should().Be(999);
            list.Should().BeEmpty();
        }
        
        [Fact]
        public void GivenBufferListWhenCapacityAchievedShouldClear()
        {
            var removedCount = 0;
            
            var list = new BufferList<int>(1000, TimeSpan.FromSeconds(1));
            list.Cleared += (removed) =>
            {
                removedCount = removed.Count();
            };
            for (var i = 0; i < 1001; i++)
            {
                list.Add(i);
            }
            
            removedCount.Should().Be(1000);
            list.Should().HaveCount(1);
        }
        
        [Fact]
        public void GivenBufferListWhenDisposeShouldClear()
        {
            var removedCount = 0;
            
            var list = new BufferList<int>(1000, TimeSpan.FromSeconds(10));
            list.Cleared += (removed) =>
            {
                removedCount = removed.Count();
            };
            for (var i = 0; i < 1000; i++)
            {
                list.Add(i);
            }
            
            list.Dispose();
            removedCount.Should().Be(1000);
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
        public void GivenBufferListWhenContainsCalledShouldVerifyItem()
        {
            var list = new BufferList<int>(1000, TimeSpan.FromSeconds(10))
            {
                1
            };

            list.Contains(1).Should().BeTrue();
        }
        
        [Fact]
        public void GivenBufferListWhenIndexOfCalledShouldReturnIndexOfItem()
        {
            var list = new BufferList<int>(1000, TimeSpan.FromSeconds(10))
            {
                1,
                2,
                3
            };

            list.IndexOf(2).Should().Be(1);
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
        public void GivenBufferListWhenGetItemFromIndexShouldReturn()
        {
            var list = new BufferList<int>(1000, TimeSpan.FromSeconds(10))
            {
                1,
                2,
                3
            };

            list[1].Should().Be(2);
        }
        
        [Fact]
        public void GivenBufferListWhenSetItemFromIndexShouldUpdate()
        {
            var list = new BufferList<int>(1000, TimeSpan.FromSeconds(10))
            {
                1,
                2,
                3
            };

            list[1] = 10;

            list[1].Should().Be(10);
        }
        
        [Fact]
        public void GivenBufferListWhenCopyToShouldCopy()
        {
            var list = new BufferList<int>(1000, TimeSpan.FromSeconds(10))
            {
                1,
                2,
                3
            };
            var target = new int[3];

            list.CopyTo(target, 0);
            target.Should().HaveCount(3);
        }
        
        [Fact]
        public void GivenBufferListWhenRemoveShouldRemoveItem()
        {
            var list = new BufferList<int>(1000, TimeSpan.FromSeconds(10))
            {
                1,
                2,
                3
            };
            list.Remove(1);

            list.Should().NotContain(1);
        }
        
        [Fact]
        public void GivenBufferListWhenRemoveFromIndexShouldRemoveItem()
        {
            var list = new BufferList<int>(1000, TimeSpan.FromSeconds(10))
            {
                1,
                2,
                3
            };
            list.RemoveAt(1);

            list.Should().NotContain(2);
        }

        [Fact]
        public void GivenBufferListWhenInsertShouldAddItem()
        {
            var list = new BufferList<int>(1000, TimeSpan.FromSeconds(10))
            {
                1,
                2,
                3
            };
            
            list.Insert(0, 0);
            
            list[0].Should().Be(0);
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
        public void GivenBufferListWhenDisposeTwiceShouldNotThrow()
        {
            var list = new BufferList<int>(1000, TimeSpan.FromSeconds(10));
            
            list.Dispose();
            list.Invoking(x => x.Dispose()).Should().NotThrow();
        }
    }
}
