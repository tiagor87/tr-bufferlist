using System;

namespace TRBufferList.Core
{
    public sealed class BufferListOptions
    {
        public BufferListOptions(int clearBatchingSize, TimeSpan idleClearTtl, int maxSize, TimeSpan disposeTimeout, TimeSpan maxSizeWaitingDelay)
        {
            ClearBatchingSize = clearBatchingSize;
            IdleClearTtl = idleClearTtl;
            MaxSize = maxSize;
            DisposeTimeout = disposeTimeout;
            MaxSizeWaitingDelay = maxSizeWaitingDelay;
        }

        public int ClearBatchingSize { get; }
        public TimeSpan IdleClearTtl { get; }
        public int MaxSize { get; }
        public TimeSpan DisposeTimeout { get; }
        public TimeSpan MaxSizeWaitingDelay { get; }
    }
}