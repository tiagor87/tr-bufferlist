using System;

namespace TRBufferList.Core
{
    public sealed class BufferListOptions
    {
        public static readonly TimeSpan MAX_DISPOSE_TIMEOUT = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan MAX_SIZE_WAITING_DELAY = TimeSpan.FromSeconds(1);
        public const int MAX_SIZE_BATCHING_MULTIPLIER = 2;
        public const int MAX_FAULT_SIZE_BATCHING_MULTIPLIER = 3;
        public BufferListOptions(int clearBatchingSize, int maxSize, int maxFaultSize, TimeSpan idleClearTtl, TimeSpan maxSizeWaitingDelay, TimeSpan disposeTimeout)
        {
            if (clearBatchingSize <= 0) throw new ArgumentException("The \"clear batching size\" must be greater than zero.", nameof(clearBatchingSize));
            if (maxSize < clearBatchingSize) throw new ArgumentException("The \"max size\" must be greater than \"clear batching size\".", nameof(maxSize));
            if (maxFaultSize < clearBatchingSize) throw new ArgumentException("The \"max fault size\" must be greater than \"clear batching size\".", nameof(maxFaultSize));
            if (maxSizeWaitingDelay > MAX_SIZE_WAITING_DELAY) throw new ArgumentException($"The \"max size waiting delay\" must be lesser than {MAX_DISPOSE_TIMEOUT.TotalSeconds}s.", nameof(maxSizeWaitingDelay));
            if (disposeTimeout > MAX_DISPOSE_TIMEOUT) throw new ArgumentException($"The \"dispose timeout\" must be lesser than {MAX_DISPOSE_TIMEOUT.TotalSeconds}s.", nameof(disposeTimeout));
            ClearBatchingSize = clearBatchingSize;
            IdleClearTtl = idleClearTtl;
            MaxSize = maxSize;
            DisposeTimeout = disposeTimeout;
            MaxSizeWaitingDelay = maxSizeWaitingDelay;
            MaxFaultSize = maxFaultSize;
        }

        public int ClearBatchingSize { get; }
        public TimeSpan IdleClearTtl { get; }
        public int MaxSize { get; }
        public TimeSpan DisposeTimeout { get; }
        public TimeSpan MaxSizeWaitingDelay { get; }
        public int MaxFaultSize { get; }

        public static BufferListOptions Simple(int clearBatchingSize, TimeSpan clearTtl)
        {
            return new BufferListOptions(
                clearBatchingSize,
                clearBatchingSize * MAX_SIZE_BATCHING_MULTIPLIER,
                clearBatchingSize * MAX_FAULT_SIZE_BATCHING_MULTIPLIER,
                clearTtl,
                MAX_SIZE_WAITING_DELAY,
                MAX_DISPOSE_TIMEOUT);
        }
    }
}