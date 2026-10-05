using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static class WarpPortableSourceServiceBanks
{
    internal const string Semantics = "warp.source-runtime-banks/closed-module-exact-frame-state-arena-eh-service-signatures/0.2";

    internal static ImmutableDictionary<MethodInfo, IReadOnlyList<WarpRuntimeWordBank>> Capture(MethodInfo entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var result = ImmutableDictionary.CreateBuilder<MethodInfo, IReadOnlyList<WarpRuntimeWordBank>>();
        var pending = new Queue<MethodInfo>(); pending.Enqueue(entry);
        while (pending.TryDequeue(out MethodInfo? method))
        {
            if (result.ContainsKey(method)) { continue; }
            if (method.Module != typeof(WarpPortableSourceServiceBanks).Module || !method.IsStatic || method.ContainsGenericParameters)
            {
                throw new ArgumentException("A source runtime service must have a closed compiler module graph.", nameof(entry));
            }
            WarpCompilationAdmission.Require(entry.Name, WarpCompilationResourceKind.Functions, result.Count + 1L, WarpCompilationAdmission.MaximumFunctionsPerEntry);
            result.Add(method, Banks(method));
            byte[] cil = method.GetMethodBody()?.GetILAsByteArray() ?? throw new ArgumentException("A source runtime service requires captured CIL.", nameof(entry));
            foreach (WarpPortableMethodGraphInstruction instruction in WarpPortableMethodGraphDecoder.Decode(cil, method.Name))
            {
                if (instruction.OpCode != OpCodes.Call) { continue; }
                MethodBase? target = method.Module.ResolveMethod((int)instruction.Operand, method.DeclaringType?.GetGenericArguments(), method.GetGenericArguments());
                if (target is not MethodInfo callee) { throw new ArgumentException("A source runtime call target must be an exact static method.", nameof(entry)); }
                pending.Enqueue(callee);
            }
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<WarpRuntimeWordBank> Banks(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        int count = parameters.Count(parameter => parameter.ParameterType == typeof(uint[]));
        if (count == 0) { return Enumerable.Repeat(WarpRuntimeWordBank.Word, parameters.Length).ToImmutableArray(); }
        bool state = method.DeclaringType == typeof(WarpPortableFrameServices) ||
            method.DeclaringType == typeof(WarpPortableHeapServices) && method.Name is "SourceStateReadByte" or "SourceStateWriteByte";
        bool mixed = method.DeclaringType == typeof(WarpPortableHeapServices) && method.Name is
            nameof(WarpPortableHeapServices.ReadSourceFrameValue) or nameof(WarpPortableHeapServices.WriteSourceFrameValue) or "ValidateSourceFrameOwner";
        bool exceptions = string.Equals(method.DeclaringType?.FullName, "WarpCLR.Compiler.WarpPortableExceptionServices", StringComparison.Ordinal);
        mixed |= exceptions && method.Name is "CaptureFrames" or "ValidateCapture" or "CopyFrames" or "PruneCaught" or
            "ApplyAction" or "ValidateTransfer" or "AcknowledgeTransfer";
        if (state ? count != 1 : mixed ? count != 2 || parameters[0].ParameterType != typeof(uint[]) || parameters[1].ParameterType != typeof(uint[]) :
            method.DeclaringType != typeof(WarpPortableHeapServices) && !exceptions || count != 1)
        {
            throw new ArgumentException("Every generated runtime array requires its explicit captured State/Arena catalog binding.", nameof(method));
        }
        int array = 0;
        return parameters.Select(parameter => parameter.ParameterType != typeof(uint[]) ? WarpRuntimeWordBank.Word :
            state || mixed && array++ == 0 ? WarpRuntimeWordBank.State : WarpRuntimeWordBank.Arena).ToImmutableArray();
    }
}
