using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests;

internal sealed partial class WarpManagedWideAtomicTests
{
    [TestMethod]
    public async Task PackedReleaseAcquirePublishesGeneratedOrdinaryDataUnderExclusiveEvents()
    {
        CoreCLRResumableKernel operation = CoreCLRResumableKernel.Compile(WarpPortableAtomicWordKernels.Arena());
        CoreCLRResumableKernel cas = CoreCLRResumableKernel.Compile(WarpManagedAtomicKernels.Create32()[2]);
        CoreCLRResumableKernel write = Witness(nameof(WarpPackedAtomicWitnessServices.WriteData));
        CoreCLRResumableKernel read = Witness(nameof(WarpPackedAtomicWitnessServices.ReadData));
        const ulong flag = 0xFEDCBA9876543210;
        foreach (int quantum in new[] { operation.Layout.MaximumBlockCost, 4096 })
        {
            uint[] arena = ProtectedArena(out uint result, out uint target);
            arena[WarpPortableSchedulerLayout.ControllerOwner] = 0;
            arena[target] = arena[target + 1] = arena[target + 2] = 0;
            Task publisher = Task.Run(async () =>
            {
                await ClaimGeneratedAsync(cas, arena, 1).ConfigureAwait(false);
                Assert.AreEqual(1u, RunProtected(write, [[WarpPortableSchedulerLayout.ControllerOwner], [1], [target + 5], [0xAABBCCDD]], arena, 4096)[WarpLogicalMachineLayout.ResultOffset]);
                uint[] state = RunProtected(operation, ProtectedArguments(target, 1, 8, 11, 0, flag, result), arena, quantum);
                Assert.AreEqual(0u, state[WarpLogicalMachineLayout.ResultOffset]);
                Assert.AreEqual(flag, Pair(arena, (int)result));
                Assert.AreEqual(1u, Run(cas, [[WarpPortableSchedulerLayout.ControllerOwner], [1], [0]], arena, 4096)[WarpLogicalMachineLayout.ResultOffset]);
            });
            ulong observed = 0;
            for (int attempt = 0; attempt < 10000 && observed == 0; attempt++)
            {
                await ClaimGeneratedAsync(cas, arena, 2).ConfigureAwait(false);
                uint[][] arguments = ProtectedArguments(target, 1, 8, 10, 0, 0, result);
                arguments[1][0] = 2;
                Assert.AreEqual(0u, RunProtected(operation, arguments, arena, quantum)[WarpLogicalMachineLayout.ResultOffset]);
                observed = Pair(arena, (int)result);
                if (observed != 0)
                {
                    Assert.AreEqual(flag, observed);
                    Assert.AreEqual(0xAABBCCDDu, RunProtected(read, [[WarpPortableSchedulerLayout.ControllerOwner], [2], [target + 5]], arena, 4096)[WarpLogicalMachineLayout.ResultOffset]);
                }
                Assert.AreEqual(2u, Run(cas, [[WarpPortableSchedulerLayout.ControllerOwner], [2], [0]], arena, 4096)[WarpLogicalMachineLayout.ResultOffset]);
                if (observed == 0) { await Task.Yield(); }
            }
            await publisher.ConfigureAwait(false);
            Assert.AreEqual(flag, observed);
        }
    }

    private static CoreCLRResumableKernel Witness(string name) => CoreCLRResumableKernel.Compile(WarpWordArenaServiceLowerer.Lower(
        typeof(WarpPackedAtomicWitnessServices).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!));
}
