using System.Text;

namespace WarpCLR.Backend.CoreCLR;

internal static class WarpCoreCLRWorkerCallObservations
{
    internal const ushort ExecuteObserved = 16;
    internal const ushort Observed = 17;
    internal const int MaximumCalls = 65536;
    internal const int CallBytes = 32;
    private const uint Magic = 0x314F4357;
    private const int TrailerBytes = sizeof(uint) + 16 + sizeof(int);

    internal static void RequireResponseCapacity(int stateWords, int arenaWords)
    {
        long bytes = checked(40L + (stateWords + (long)arenaWords) * sizeof(uint) + TrailerBytes + MaximumCalls * (long)CallBytes);
        if (bytes > WarpCoreCLRWorkerWords.MaximumBytes)
        { throw new InvalidDataException("The finite call-observation response exceeds transfer admission before execution."); }
    }

    internal static byte[] Response(byte[] request, uint[] state, uint[] arena, Guid module, IReadOnlyList<CallUse> calls)
    {
        RequireResponseCapacity(state.Length, arena.Length);
        if (module == Guid.Empty || calls.Count > MaximumCalls)
        { throw new InvalidDataException("The observation module or call count is outside its exact admission."); }
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        // Preserve SHA(request) at byte zero and the ordinary state/arena prefix.
        writer.Write(WarpCoreCLRWorkerWords.Response(request, state, arena));
        writer.Write(Magic); writer.Write(module.ToByteArray()); writer.Write(calls.Count);
        foreach (CallUse call in calls)
        {
            writer.Write(call.Function); writer.Write(call.ProgramCounter); writer.Write(call.Callee); writer.Write(call.Continuation);
            writer.Write(call.Frame); writer.Write(call.Depth); writer.Write(call.Activation); writer.Write(call.ParentActivation);
        }
        writer.Flush(); return stream.ToArray();
    }

    internal static Result ReadResponse(byte[] payload, byte[] request, int stateWords, int arenaWords, Guid exactModule)
    {
        RequireResponseCapacity(stateWords, arenaWords);
        int prefix = checked(40 + (stateWords + arenaWords) * sizeof(uint));
        if (payload.Length < prefix + TrailerBytes || payload.Length > WarpCoreCLRWorkerWords.MaximumBytes)
        { throw new InvalidDataException("An authenticated call-observation result has an invalid complete length."); }
        (uint[] state, uint[] arena) = WarpCoreCLRWorkerWords.ReadResponse(payload[..prefix], request, stateWords, arenaWords);
        using var stream = new MemoryStream(payload, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        stream.Position = prefix;
        if (reader.ReadUInt32() != Magic || new Guid(reader.ReadBytes(16)) != exactModule || exactModule == Guid.Empty)
        { throw new InvalidDataException("The actual observed emitted module differs from the compiled Worker."); }
        int count = reader.ReadInt32();
        if (count < 0 || count > MaximumCalls || stream.Length - stream.Position != count * (long)CallBytes)
        { throw new InvalidDataException("The complete observed call inventory was truncated, extended or exceeds admission."); }
        var calls = new CallUse[count];
        foreach (ref CallUse call in calls.AsSpan())
        {
            call = new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32());
        }
        return new(state, arena, calls);
    }

    internal sealed record CallUse(int Function, int ProgramCounter, int Callee, int Continuation, int Frame,
        uint Depth, uint Activation, uint ParentActivation);
    internal sealed record Result(uint[] State, uint[] Arena, CallUse[] Calls);
}
