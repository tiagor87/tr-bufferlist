using System;
using FluentAssertions;
using Xunit;

namespace TRBufferList.Core.Tests
{
    public class BufferListOptionsTests
    {
        [Fact]
        public void GivenOptionsWhenCreateSimpleShouldSetProperties()
        {
            var options = BufferListOptions.Simple(
                100,
                TimeSpan.FromMilliseconds(5000));

            options.ClearBatchingSize.Should().Be(100);
            options.MaxSize.Should().Be(200);
            options.MaxFaultSize.Should().Be(300);
            options.IdleClearTtl.Should().Be(TimeSpan.FromMilliseconds(5000));
            options.DisposeTimeout.Should().Be(TimeSpan.FromSeconds(10));
            options.MaxSizeWaitingDelay.Should().Be(TimeSpan.FromSeconds(1));
        }
        
        [Fact]
        public void GivenOptionsWhenCreateShouldSetProperties()
        {
            var options = new BufferListOptions(
                100,
                500,
                1000,
                TimeSpan.FromMilliseconds(5000),
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromSeconds(10));

            options.ClearBatchingSize.Should().Be(100);
            options.MaxSize.Should().Be(500);
            options.MaxFaultSize.Should().Be(1000);
            options.IdleClearTtl.Should().Be(TimeSpan.FromMilliseconds(5000));
            options.MaxSizeWaitingDelay.Should().Be(TimeSpan.FromMilliseconds(100));
            options.DisposeTimeout.Should().Be(TimeSpan.FromSeconds(10));
        }
        
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void GivenOptionsWhenClearBatchingSizeInvalidShouldThrow(int clearBatchingSize)
        {
            Action action = () => new BufferListOptions(
                clearBatchingSize,
                500,
                1000,
                TimeSpan.FromMilliseconds(5000),
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromSeconds(10));

            action.Should().Throw<ArgumentException>()
                .WithMessage("The \"clear batching size\" must be greater than zero.*");
        }
        
        [Theory]
        [InlineData(1, 0)]
        [InlineData(1, -1)]
        public void GivenOptionsWhenMaxSizeInvalidShouldThrow(int clearBatchingSize, int maxSize)
        {
            Action action = () => new BufferListOptions(
                clearBatchingSize,
                maxSize,
                1000,
                TimeSpan.FromMilliseconds(5000),
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromSeconds(10));

            action.Should().Throw<ArgumentException>()
                .WithMessage("The \"max size\" must be greater than \"clear batching size\".*");
        }
        
        [Theory]
        [InlineData(1, 0)]
        [InlineData(1, -1)]
        public void GivenOptionsWhenMaxFaultSizeInvalidShouldThrow(int clearBatchingSize, int maxFaultSize)
        {
            Action action = () => new BufferListOptions(
                clearBatchingSize,
                clearBatchingSize,
                maxFaultSize,
                TimeSpan.FromMilliseconds(5000),
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromSeconds(10));

            action.Should().Throw<ArgumentException>()
                .WithMessage("The \"max fault size\" must be greater than \"clear batching size\".*");
        }
        
        [Theory]
        [InlineData(1001)]
        [InlineData(1500)]
        public void GivenOptionsWhenMaxSizeWaitingDelayInvalidShouldThrow(int maxSizeWaitingDelayInMilliseconds)
        {
            Action action = () => new BufferListOptions(
                1,
                1,
                1,
                TimeSpan.FromMilliseconds(5000),
                TimeSpan.FromMilliseconds(maxSizeWaitingDelayInMilliseconds),
                TimeSpan.FromSeconds(10));

            action.Should().Throw<ArgumentException>()
                .WithMessage("The \"max size waiting delay\" must be lesser than *s.*");
        }
        
        [Theory]
        [InlineData(10001)]
        [InlineData(15000)]
        public void GivenOptionsWhenDisposeTimeoutInvalidShouldThrow(int disposeTimeoutInMilliseconds)
        {
            Action action = () => new BufferListOptions(
                1,
                1,
                1,
                TimeSpan.FromMilliseconds(5000),
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(disposeTimeoutInMilliseconds));

            action.Should().Throw<ArgumentException>()
                .WithMessage("The \"dispose timeout\" must be lesser than *s.*");
        }
    }
}