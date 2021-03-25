using System;

namespace TRBufferList.Core
{
    [Flags]
    public enum BufferListGcCollectionOptions
    {
        None = 0,
        EachFullClearExecution = 1,
        EachBatchingClearExecution = 2
    }
}