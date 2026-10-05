using WarpCLR.IR;

namespace WarpCLR.Compiler;

public sealed partial class WarpPortableMachineEmitter
{
    private sealed partial class Emission
    {
        private void AppendManagedException(WarpLogicalMachineNode node, WarpManagedExceptionTerminator managed)
        {
            string context = LoadValue(managed.Context);
            string objectId = LoadValue(managed.ObjectId);
            string generation = LoadValue(managed.Generation);
            string valid = Assign($"and i1 {Assign($"icmp ne i32 {context}, 0")}, {Assign($"icmp ne i32 {objectId}, 0")}");
            valid = Assign($"and i1 {valid}, {Assign($"icmp ne i32 {generation}, 0")}");
            string suffix = N(node.ProgramCounter);
            Line($"  br i1 {valid}, label %managed_exception_{suffix}, label %managed_exception_invalid_{suffix}");
            Line($"managed_exception_{suffix}:");
            StoreHeader(WarpLogicalMachineLayout.EscapedExceptionContextOffset, context);
            StoreHeader(WarpLogicalMachineLayout.EscapedExceptionObjectOffset, objectId);
            StoreHeader(WarpLogicalMachineLayout.EscapedExceptionGenerationOffset, generation);
            StoreHeader(WarpLogicalMachineLayout.SourceBoundaryStateOffset, "0");
            AppendFault(node, WarpLogicalMachineLayout.ManagedExceptionFault);
            Line($"managed_exception_invalid_{suffix}:");
            AppendFault(node, 3);
        }
    }
}
