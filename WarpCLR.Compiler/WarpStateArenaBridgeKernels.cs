using System.Reflection;
using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static class WarpStateArenaBridgeKernels
{
    internal static IReadOnlyList<WarpLogicalMachineLayout> Create()
    {
        MethodInfo direct = typeof(WarpStateArenaBridgeServices).GetMethod(nameof(WarpStateArenaBridgeServices.CopyOwnedWords))!;
        MethodInfo indirect = typeof(WarpStateArenaBridgeServices).GetMethod(nameof(WarpStateArenaBridgeServices.CopyOwnedAddress))!;
        MethodInfo readState = typeof(WarpStateArenaBridgeServices).GetMethod(nameof(WarpStateArenaBridgeServices.ReadStateWord))!;
        MethodInfo writeArena = typeof(WarpStateArenaBridgeServices).GetMethod(nameof(WarpStateArenaBridgeServices.WriteArenaWord))!;
        return [WarpWordStateArenaServiceLowerer.Lower(direct, new Dictionary<MethodInfo, IReadOnlyList<WarpRuntimeWordBank>>
            {
                [direct] = [WarpRuntimeWordBank.State, WarpRuntimeWordBank.Arena, WarpRuntimeWordBank.Word],
                [readState] = [WarpRuntimeWordBank.State, WarpRuntimeWordBank.Word],
                [writeArena] = [WarpRuntimeWordBank.Arena, WarpRuntimeWordBank.Word, WarpRuntimeWordBank.Word],
            }), WarpWordStateArenaServiceLowerer.Lower(indirect, new Dictionary<MethodInfo, IReadOnlyList<WarpRuntimeWordBank>>
            {
                [indirect] = [WarpRuntimeWordBank.State, WarpRuntimeWordBank.Arena, WarpRuntimeWordBank.Word],
            })];
    }
}
