using System;

namespace TRBufferList.Core
{
    public sealed class BufferListOptions
    {
        public BufferListOptions(int clearBatchingSize, TimeSpan idleClearTtl, int maxSize, TimeSpan disposeTimeout, TimeSpan maxSizeWaitingDelay, int maxFailureSize)
        {
            ClearBatchingSize = clearBatchingSize;
            IdleClearTtl = idleClearTtl;
            MaxSize = maxSize;
            DisposeTimeout = disposeTimeout;
            MaxSizeWaitingDelay = maxSizeWaitingDelay;
            MaxFailureSize = maxFailureSize;
        }

        public int ClearBatchingSize { get; }
        public TimeSpan IdleClearTtl { get; }
        public int MaxSize { get; }
        public TimeSpan DisposeTimeout { get; }
        public TimeSpan MaxSizeWaitingDelay { get; }
        public int MaxFailureSize { get; }
    }
}