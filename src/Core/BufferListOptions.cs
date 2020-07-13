using System;

namespace TRBufferList.Core
{
    public sealed class BufferListOptions
    {
        public static readonly TimeSpan MAX_DISPOSE_TIMEOUT = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan MAX_SIZE_WAITING_DELAY = TimeSpan.FromSeconds(1);
        public const int MAX_SIZE_BATCHING_MULTIPLIER = 2;
        public const int MAX_FAULT_SIZE_BATCHING_MULTIPLIER = 3;
        
        /// <summary>
        /// Instantiate options.
        /// </summary>
        /// <param name="clearBatchingSize">Maximum size of the list processed during cleaning.</param>
        /// <param name="maxSize">Maximum size allowed by the list.</param>
        /// <param name="maxFaultSize">Maximum size of the fault list.</param>
        /// <param name="idleClearTtl">Expected time to perform cleaning when no items are added.</param>
        /// <param name="maxSizeWaitingDelay">Waiting time in each scan iteration for processing when the list is overloaded.</param>
        /// <param name="disposeTimeout">Maximum waiting time to perform a complete clean-up during disposal.</param>
        /// <exception cref="ArgumentException">Occurred when any parameter is invalid.</exception>
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

        /// <summary>
        /// Get maximum size of the list processed during cleaning.
        /// </summary>
        public int ClearBatchingSize { get; }
        
        /// <summary>
        /// Get maximum size allowed by the list.
        /// </summary>
        public int MaxSize { get; }
        /// <summary>
        /// Get maximum size of the fault list.
        /// </summary>
        public int MaxFaultSize { get; }
        /// <summary>
        /// Get expected time to perform cleaning when no items are added.
        /// </summary>
        public TimeSpan IdleClearTtl { get; }
        /// <summary>
        /// Get waiting time in each scan iteration for processing when the list is overloaded.
        /// </summary>
        public TimeSpan MaxSizeWaitingDelay { get; }
        /// <summary>
        /// Get maximum waiting time to perform a complete clean-up during disposal.
        /// </summary>
        public TimeSpan DisposeTimeout { get; }

        /// <summary>
        /// Create a default options instance with minimum parameters.
        /// </summary>
        /// <param name="clearBatchingSize">Maximum size of the list processed during cleaning.</param>
        /// <param name="clearTtl">Expected time to perform cleaning when no items are added.</param>
        /// <returns></returns>
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