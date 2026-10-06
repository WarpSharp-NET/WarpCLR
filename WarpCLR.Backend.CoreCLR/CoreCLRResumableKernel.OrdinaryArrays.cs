using System.Reflection.Emit;
using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

public sealed partial class CoreCLRResumableKernel
{
    private sealed partial class QuantumEmitter
    {
        // Every array-bearing generated entry uses this denial guard, including private
        // profiles. It grants no private purpose; the ordinary wrapper still rejects those.
        private readonly CoreCLROrdinaryArrayEmission ordinary;

        private void EmitQuantumReturn() => ordinary.Return();

        private void LoadCapturedInput(int index) => ordinary.LoadInput(index);

        private void EmitOrdinaryTerminalReturn()
        {
            Label runnable = il.DefineLabel();
            Label terminal = il.DefineLabel();
            LoadState(WarpLogicalMachineLayout.StatusOffset);
            il.Emit(OpCodes.Switch, new[] { runnable, terminal, terminal });
            il.Emit(OpCodes.Ldstr, "A logical machine status is invalid.");
            il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
            il.Emit(OpCodes.Throw);
            il.MarkLabel(terminal);
            EmitQuantumReturn();
            il.MarkLabel(runnable);
        }
    }
}
