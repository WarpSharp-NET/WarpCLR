using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal static class WarpDispatchResourceAdmission
{
    internal static long EstimateBufferBytes(WarpLogicalMachineLayout layout, int count, WarpRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        try
        {
            long inputWords = (long)count * layout.Kernel.InputBufferCount;
            int residentCount = Math.Min(count, options.MaximumResidentWorkers);
            long stateWords = (long)residentCount * layout.GetStateWords(options.MaximumCallDepth);
            if (stateWords > Array.MaxLength)
            {
                throw new WarpHostException("WRPRUNTIME1005", "The resident logical-state batch exceeds the common representable array resource limit.");
            }

            long reductionWords = layout.Kernel.Reduction.HasValue ? count + ((long)count + 1) / 2 : 0;
            long transportWords = (layout.Kernel.InputBufferCount + 5L) * 8 + layout.Kernel.ScalarArgumentCount * 6L + residentCount * 4L;

            // Reserve the same conservative UInt32 working-set bound on every backend:
            // host/device inputs, unpublished host output, three host state copies plus
            // resident device state, reduction ping-pong buffers, scalar staging,
            // 64-bit argument/pointer tables and per-worker budget validation.
            // This is a buffer-data quota, not a claim about CLR object-header/GC overhead.
            return checked((inputWords * 2 + count + stateWords * 4 + reductionWords + transportWords) * sizeof(uint));
        }
        catch (OverflowException exception)
        {
            throw new WarpHostException("WRPRUNTIME1005", "The dispatch exceeds the representable buffer or logical-stack resources.", exception);
        }
    }
}
