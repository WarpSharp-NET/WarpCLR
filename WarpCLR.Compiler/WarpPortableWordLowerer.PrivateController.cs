using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    private sealed partial class Builder
    {
        private WarpPrivateControllerProjection CapturePrivateControllerProjection()
        {
            var uses = new List<WarpPrivateControllerUse>();
            foreach (WarpControlFlowFunction function in functions.OfType<WarpControlFlowFunction>())
            {
                foreach (WarpBasicBlock block in function.Blocks)
                {
                    foreach (WarpIrInstruction load in block.Instructions.Where(item => item.OpCode == WarpPrivateControllerOpCode.LoadController))
                    {
                        WarpIrInstruction[] calls = block.Instructions.Where(item => item.OpCode == WarpIrOpCode.Call && item.Arguments.Contains(load.Result)).ToArray();
                        if (calls.Length != 1)
                        { throw Error(function.Name, "A private controller load must bind one exact runtime service call.", 0); }
                        WarpIrInstruction call = calls[0];
                        int[] arguments = call.Arguments.ToArray();
                        int argument = Array.IndexOf(arguments, load.Result);
                        uses.Add(new(function.Id + 1, block.Id, load.Result, call.Callee, argument, call.Result,
                            functions[call.Callee]!.Name));
                    }
                }
            }
            services.Add(WarpPrivateControllerOpCode.Version);
            return new(uses);
        }
    }
}
