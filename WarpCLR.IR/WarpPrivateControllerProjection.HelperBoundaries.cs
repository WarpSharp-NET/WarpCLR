namespace WarpCLR.IR;

internal sealed partial class WarpPrivateControllerProjection
{
    internal const string HelperBoundarySemantics = "warp.private-helper-boundaries/exact-zero-source-charge-first-load-pure-call-explicit-phase-ack-fresh-nonzero-word/0.1";

    internal bool RequiresHelperBoundaries { get; }
    internal bool RequiresHelperReturnFences { get; }

    internal const string HelperReturnFenceSemantics = "warp.private-helper-return-fence/exact-mode1-bound-header28-31-caller-helper-activation-scope-before-tuple-and-all-successors-phase5-phase6-zero-word-resume/0.2";

    internal bool IsHelperBoundary(int function, int block) => RequiresHelperBoundaries &&
        Uses.Any(use => use.Function == function && use.Block == block);

    private void ValidateHelperBridge(int function, WarpBasicBlock block, IReadOnlyList<WarpLogicalBodyMetadata> metadata)
    {
        WarpIrInstruction[] loads = block.Instructions.Where(instruction => instruction.OpCode == WarpPrivateControllerOpCode.LoadController).ToArray();
        if (loads.Length == 0) { return; }
        WarpPrivateControllerUse? use = Uses.FirstOrDefault(item => item.Function == function && item.Block == block.Id && item.Value == loads[0].Result);
        if (use is null || loads.Length != 1 || block.Instructions[0].OpCode != WarpPrivateControllerOpCode.LoadController ||
            metadata[function].SourceBlockCosts[block.Id] != 0 || metadata[function].RuntimeHelper ||
            block.Instructions.Count(instruction => instruction.OpCode == WarpIrOpCode.Call) != 1 ||
            block.Instructions.First(instruction => instruction.OpCode == WarpIrOpCode.Call).Result != use.CallValue ||
            block.Terminator is WarpStateDispatchTerminator or WarpManagedExceptionTerminator)
        {
            throw new ArgumentException("A private helper boundary needs one first load in an exact zero-charge nonhelper bridge and one pure service call.", nameof(block));
        }
    }
}
