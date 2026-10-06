using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

internal static partial class WarpCoreCLRBinaryPlanCodec
{
    private static void WriteExecution(BinaryWriter writer, WarpLogicalExecutionMetadata? execution)
    {
        writer.Write(execution is not null);
        if (execution is null) { return; }
        writer.Write(execution.RecursiveCalls); writer.Write(execution.FrameOwners); writer.Write(execution.RuntimeStateAccess); writer.Write(execution.NonlocalStateDispatch);
        writer.Write(execution.ManagedExceptionTermination); writer.Write(execution.LogicalWorkerAccess);
        if (execution.LogicalWorkerAccess) { WriteString(writer, WarpManagedInvocationOpCode.Version); }
        if (execution.PrivateControllerProjection is { } projection)
        {
            WriteString(writer, WarpPrivateControllerOpCode.Version);
            if (projection.RequiresHelperReturnFences)
            {
                WriteString(writer, WarpPrivateControllerProjection.HelperReturnFenceSemantics);
                WriteString(writer, WarpLogicalMachineLayout.PrivateHelperScopeVersion);
            }
            if (projection.RequiresHelperBoundaries) { WriteString(writer, WarpPrivateControllerProjection.HelperBoundarySemantics); }
            writer.Write(projection.Uses.Count);
            foreach (WarpPrivateControllerUse use in projection.Uses)
            {
                writer.Write(use.Function); writer.Write(use.Block); writer.Write(use.Value);
                writer.Write(use.Callee); writer.Write(use.Argument); writer.Write(use.CallValue);
                WriteString(writer, use.ServiceIdentity);
            }
        }
        writer.Write(execution.Bodies.Count);
        foreach (WarpLogicalBodyMetadata body in execution.Bodies)
        {
            writer.Write(body.PrivateWordCount); writer.Write(body.RuntimeHelper); writer.Write(body.CountsSourceDepth);
            writer.Write(body.AliasOwnerFunction); writer.Write(body.AliasPrefixWords); WriteValues(writer, body.SourceBlockCosts);
        }
    }

    private static WarpLogicalExecutionMetadata? ReadExecution(BinaryReader reader, int bodies, string version)
    {
        if (!Flag(reader))
        {
            if (!string.Equals(version, WarpLogicalExecutionMetadata.Version, StringComparison.Ordinal)) { throw new InvalidDataException("Private controller metadata is absent."); }
            return null;
        }
        bool recursive = Flag(reader), owners = Flag(reader), stateAccess = Flag(reader), nonlocal = Flag(reader);
        bool managedException = Flag(reader), logicalWorker = Flag(reader);
        if (logicalWorker && !string.Equals(ReadString(reader), WarpManagedInvocationOpCode.Version, StringComparison.Ordinal))
        { throw new InvalidDataException("The logical-worker invocation capability version is unsupported."); }
        bool returnFences = string.Equals(version, WarpLogicalExecutionMetadata.PrivateHelperReturnFenceVersion, StringComparison.Ordinal);
        bool helperBoundaries = returnFences || string.Equals(version, WarpLogicalExecutionMetadata.PrivateHelperBoundaryVersion, StringComparison.Ordinal);
        WarpPrivateControllerProjection? projection = helperBoundaries || string.Equals(version, WarpLogicalExecutionMetadata.PrivateControllerVersion, StringComparison.Ordinal) ?
            ReadPrivateControllerProjection(reader, helperBoundaries, returnFences) : null;
        if (Count(reader, WarpCompilationAdmission.MaximumFunctionsPerEntry + 1) != bodies) { throw new InvalidDataException("Execution body count mismatch."); }
        var metadata = new WarpLogicalBodyMetadata[bodies];
        foreach (ref WarpLogicalBodyMetadata body in metadata.AsSpan())
        {
            int privateWords = Count(reader, WarpCompilationAdmission.MaximumValueSlotsPerEntry);
            bool helper = Flag(reader), sourceDepth = Flag(reader);
            int aliasOwner = reader.ReadInt32();
            int aliasPrefix = Count(reader, WarpCompilationAdmission.MaximumValueSlotsPerEntry);
            int length = Count(reader, WarpCompilationAdmission.MaximumBlocksPerEntry);
            if (length * 4L > reader.BaseStream.Length - reader.BaseStream.Position) { throw new InvalidDataException("Truncated source costs."); }
            int[] costs = new int[length];
            foreach (ref int cost in costs.AsSpan()) { cost = reader.ReadInt32(); }
            body = new(privateWords, helper, costs, sourceDepth, aliasOwner, aliasPrefix);
        }
        return new(metadata, recursive, owners, stateAccess, nonlocal, managedException, logicalWorker, projection);
    }

    private static WarpPrivateControllerProjection ReadPrivateControllerProjection(BinaryReader reader, bool helperBoundaries, bool returnFences)
    {
        if (!string.Equals(ReadString(reader), WarpPrivateControllerOpCode.Version, StringComparison.Ordinal))
        { throw new InvalidDataException("The private controller capability version is unsupported."); }
        if (returnFences && (!string.Equals(ReadString(reader), WarpPrivateControllerProjection.HelperReturnFenceSemantics, StringComparison.Ordinal) ||
            !string.Equals(ReadString(reader), WarpLogicalMachineLayout.PrivateHelperScopeVersion, StringComparison.Ordinal)))
        { throw new InvalidDataException("The private helper-return-fence semantics are unsupported."); }
        if (helperBoundaries && !string.Equals(ReadString(reader), WarpPrivateControllerProjection.HelperBoundarySemantics, StringComparison.Ordinal))
        { throw new InvalidDataException("The private helper-boundary semantics are unsupported."); }
        int count = Count(reader, WarpCompilationAdmission.MaximumInstructionsPerEntry);
        if (count * 28L > reader.BaseStream.Length - reader.BaseStream.Position)
        { throw new InvalidDataException("Private controller uses are truncated."); }
        var uses = new WarpPrivateControllerUse[count];
        foreach (ref WarpPrivateControllerUse use in uses.AsSpan())
        {
            use = new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadInt32(), reader.ReadInt32(), ReadString(reader));
        }
        return new(uses, helperBoundaries, returnFences);
    }

    private static WarpControlFlowKernel ReadPlan(BinaryReader reader, string expectedIrHash)
    {
        bool returnFences = ReadWireProfile(reader);
        if (!string.Equals(ReadString(reader), WarpProfileCatalog.ProfileId, StringComparison.Ordinal) ||
            !string.Equals(ReadString(reader), WarpRuntimeAbi.Version, StringComparison.Ordinal) ||
            !string.Equals(ReadString(reader), WarpRuntimeAbi.SafepointPolicy, StringComparison.Ordinal) ||
            !string.Equals(ReadString(reader), WarpLogicalMachineLayout.Version, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The binary plan profile/ABI/metadata schema is unsupported.");
        }
        string executionVersion = ReadString(reader);
        if (returnFences != string.Equals(executionVersion, WarpLogicalExecutionMetadata.PrivateHelperReturnFenceVersion, StringComparison.Ordinal))
        { throw new InvalidDataException("The private helper-return-fence metadata and wire profile must match exactly."); }
        if (!string.Equals(executionVersion, WarpLogicalExecutionMetadata.Version, StringComparison.Ordinal) &&
            !string.Equals(executionVersion, WarpLogicalExecutionMetadata.PrivateControllerVersion, StringComparison.Ordinal) &&
            !string.Equals(executionVersion, WarpLogicalExecutionMetadata.PrivateHelperBoundaryVersion, StringComparison.Ordinal) &&
            !string.Equals(executionVersion, WarpLogicalExecutionMetadata.PrivateHelperReturnFenceVersion, StringComparison.Ordinal))
        { throw new InvalidDataException("The binary logical metadata version is unsupported."); }
        string hash = ReadString(reader), name = ReadString(reader);
        if (!string.Equals(hash, expectedIrHash, StringComparison.Ordinal)) { throw new InvalidDataException("The binary plan changes its admitted IR identity."); }
        int inputs = Count(reader, WarpCompilationAdmission.MaximumParametersPerBody);
        int scalars = Count(reader, WarpCompilationAdmission.MaximumParametersPerBody);
        int reduction = reader.ReadInt32();
        if (reduction < -1 || reduction >= 0 && !Enum.IsDefined((WarpReductionOperation)reduction)) { throw new InvalidDataException("Unsupported reduction identity."); }
        int length = Count(reader, WarpCompilationAdmission.MaximumFunctionsPerEntry);
        var functions = new WarpControlFlowFunction[length];
        var usage = new Usage();
        foreach (ref WarpControlFlowFunction function in functions.AsSpan())
        {
            int id = reader.ReadInt32(); string identity = ReadString(reader);
            int arguments = Count(reader, WarpCompilationAdmission.MaximumParametersPerBody);
            function = new(id, identity, arguments, ReadBody(reader, usage));
        }
        WarpBasicBlock[] blocks = ReadBody(reader, usage);
        WarpLogicalExecutionMetadata? execution = ReadExecution(reader, length + 1, executionVersion);
        if (reader.BaseStream.Position != reader.BaseStream.Length) { throw new InvalidDataException("Trailing binary plan data."); }
        var kernel = new WarpControlFlowKernel(name, inputs, scalars, blocks,
            reduction < 0 ? null : (WarpReductionOperation)reduction, functions, execution);
        WarpCompilationAdmission.Validate(kernel);
        if (!string.Equals(WarpIrHash.Compute(kernel), expectedIrHash, StringComparison.Ordinal)) { throw new InvalidDataException("Binary plan reconstruction changed its IR hash."); }
        return kernel;
    }
}
