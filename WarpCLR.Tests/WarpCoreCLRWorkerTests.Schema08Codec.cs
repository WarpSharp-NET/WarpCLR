using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    [TestMethod]
    public void NewCodecRejectsOldSchemasAndUnknownInvocationVersionWithAValidDigest()
    {
        WarpControlFlowKernel kernel = WarpLogicalWorkerHookKernels.CreateDirect().Kernel;
        byte[] original = WarpCoreCLRBinaryPlanCodec.Serialize(kernel);
        foreach (string identity in new[] { WarpCoreCLRBinaryPlanCodec.Version, WarpLogicalMachineLayout.Version,
            WarpLogicalExecutionMetadata.Version, WarpManagedInvocationOpCode.Version })
        {
            byte[] mutated = (byte[])original.Clone();
            int offset = mutated.AsSpan().IndexOf(Encoding.Unicode.GetBytes(identity));
            Assert.IsGreaterThanOrEqualTo(0, offset);
            mutated[offset + (identity.Length - 1) * sizeof(ushort)] = (byte)'0';
            RejectAuthenticatedPlan(kernel, mutated);
        }
        byte[] oldMagic = (byte[])original.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(oldMagic, 0x57425032);
        RejectAuthenticatedPlan(kernel, oldMagic);
        byte[] trailing = new byte[original.Length + 1];
        original.AsSpan(0, original.Length - 32).CopyTo(trailing);
        RejectAuthenticatedPlan(kernel, trailing);
    }

    [TestMethod]
    public void NewCodecRejectsNoncanonicalCapabilitiesAliasAndCallPadding()
    {
        WarpControlFlowKernel alias = WarpFilterAliasHookKernels.Create().Kernel;
        byte[] original = WarpCoreCLRBinaryPlanCodec.Serialize(alias);
        int start = ExecutionOffset(alias, original);
        for (int flag = 0; flag < 7; flag++)
        {
            byte[] badFlag = (byte[])original.Clone(); badFlag[start + flag] = 2;
            RejectAuthenticatedPlan(alias, badFlag);
        }
        int body = start + 11 + alias.Execution!.Bodies.Take(2).Sum(item => 18 + item.SourceBlockCosts.Count * 4);
        foreach ((int offset, int value) in new[] { (body + 6, -1), (body + 10, 3), (body, int.MaxValue) })
        {
            byte[] badAlias = (byte[])original.Clone(); BinaryPrimitives.WriteInt32LittleEndian(badAlias.AsSpan(offset), value);
            RejectAuthenticatedPlan(alias, badAlias);
        }
        foreach (int offset in new[] { body + 4, body + 5 })
        {
            byte[] badAlias = (byte[])original.Clone(); badAlias[offset] = 1;
            RejectAuthenticatedPlan(alias, badAlias);
        }
        RejectNoncanonicalCallPadding();
    }

    [TestMethod]
    public void NewCodecRejectsManagedTerminalWithoutItsCapabilityOrOperands()
    {
        WarpControlFlowKernel kernel = WarpManagedExceptionHookKernels.Create(3).Kernel;
        byte[] original = WarpCoreCLRBinaryPlanCodec.Serialize(kernel);
        int metadata = ExecutionOffset(kernel, original);
        byte[] missing = (byte[])original.Clone(); missing[metadata + 5] = 0;
        RejectAuthenticatedPlan(kernel, missing);
        foreach ((int offset, int value) in new[] { (metadata - 4, -1), (metadata - 8, 3) })
        {
            byte[] badTerminal = (byte[])original.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(badTerminal.AsSpan(offset), value);
            RejectAuthenticatedPlan(kernel, badTerminal);
        }
    }

    private static int ExecutionOffset(WarpControlFlowKernel kernel, byte[] bytes)
    {
        WarpLogicalExecutionMetadata execution = kernel.Execution!;
        int length = 11 + execution.Bodies.Sum(body => 18 + body.SourceBlockCosts.Count * 4);
        if (execution.LogicalWorkerAccess) { length += 4 + Encoding.Unicode.GetByteCount(WarpManagedInvocationOpCode.Version); }
        return bytes.Length - 32 - length;
    }

    private static void RejectAuthenticatedPlan(WarpControlFlowKernel original, byte[] mutated)
    {
        SHA256.HashData(mutated.AsSpan(0, mutated.Length - 32), mutated.AsSpan(mutated.Length - 32));
        Assert.ThrowsExactly<InvalidDataException>(() => WarpCoreCLRBinaryPlanCodec.Deserialize(mutated, WarpIrHash.Compute(original)));
    }

    private static void RejectNoncanonicalCallPadding()
    {
        WarpControlFlowKernel kernel = RichKernel();
        byte[] original = WarpCoreCLRBinaryPlanCodec.Serialize(kernel);
        WarpIrInstruction call = kernel.Instructions.First(item => item.OpCode == WarpIrOpCode.Call);
        using var needle = new MemoryStream();
        using var writer = new BinaryWriter(needle, Encoding.UTF8, leaveOpen: true);
        writer.Write(call.Result); writer.Write((int)call.OpCode); writer.Write((int)call.ResultType);
        writer.Write(call.Left); writer.Write(call.Right); writer.Write(call.Immediate); writer.Write(call.Third);
        writer.Write(call.Callee); writer.Write(call.ResultWordCount);
        writer.Flush();
        int instruction = original.AsSpan().IndexOf(needle.ToArray());
        Assert.IsGreaterThanOrEqualTo(0, instruction);
        foreach (int field in new[] { 8, 12, 16, 20, 24 })
        {
            byte[] padding = (byte[])original.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(padding.AsSpan(instruction + field), 17);
            RejectAuthenticatedPlan(kernel, padding);
        }
    }
}
