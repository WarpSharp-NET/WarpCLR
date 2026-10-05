using System.Security.Cryptography;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    private sealed class LateCleanupBindings : IAsyncDisposable
    {
        private readonly WarpCoreCLRWorkerKernel casKernel;
        private readonly WarpCoreCLRWorkerKernel censusKernel;
        private readonly WarpCoreCLRWorkerLease casLease;
        private readonly WarpCoreCLRWorkerLease censusLease;

        private LateCleanupBindings(WarpCoreCLRWorkerKernel cas, WarpCoreCLRWorkerKernel census, WarpLogicalMachineLayout source)
        {
            casKernel = cas; censusKernel = census;
            casLease = cas.TryAcquireLease()!; censusLease = census.TryAcquireLease()!;
            const uint controller = 119;
            uint[] arena = SeedRecoveryArena(3, 1, true, controller);
            uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
            string schema = Convert.ToHexString(SHA256.HashData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                arena.AsSpan((int)(scheduler + WarpPortableSchedulerLayout.SchemaHash), 8))));
            string ir = WarpIrHash.Compute(source.Kernel);
            Admission = new(new object(), source.CreateInitialState(1, long.MaxValue), arena, schema, ir, ir,
                arena[scheduler + WarpPortableSchedulerLayout.DispatchGeneration], arena[scheduler + WarpPortableSchedulerLayout.GCEpoch],
                controller, RecoveryBindings(arena, scheduler, controller, true, casLease, censusLease));
        }

        internal WarpCoreCLRCommandAdmission Admission { get; }

        internal static async Task<LateCleanupBindings> CreateAsync(WarpLogicalMachineLayout source)
        {
            WarpCoreCLRWorkerKernel cas = await WarpCoreCLRWorkerKernel.CompileAsync(WarpManagedAtomicKernels.Create32()[2], new(), CancellationToken.None).ConfigureAwait(false);
            WarpCoreCLRWorkerKernel? census = null;
            try
            {
                WarpLogicalMachineLayout layout = WarpWordArenaServiceLowerer.Lower(typeof(WarpPortableSchedulerServices)
                    .GetMethod(nameof(WarpPortableSchedulerServices.DisposeStoppedCensus))!);
                census = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
                return new(cas, census, source);
            }
            catch
            {
                if (census is not null) { await census.DisposeAsync().ConfigureAwait(false); }
                await cas.DisposeAsync().ConfigureAwait(false); throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await casLease.DisposeAsync().ConfigureAwait(false); await censusLease.DisposeAsync().ConfigureAwait(false);
            await casKernel.DisposeAsync().ConfigureAwait(false); await censusKernel.DisposeAsync().ConfigureAwait(false);
        }
    }
}
