using System.Reflection;
using System.Text.Json;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Production;

// Optional artifact export for this component's exact test execution boundary.
// It serializes admitted generated IR and immutable maps; it never invokes source.
internal static class WarpCompiledSourceEvidence
{
    private static readonly Lock Gate = new();
    private static readonly HashSet<string> Written = new(StringComparer.Ordinal);

    internal static void Capture(WarpCompiledSourcePlan plan)
    {
        string? directory = Environment.GetEnvironmentVariable("WARP_COMPILED_HOST_EVIDENCE");
        if (directory is null) { return; }
        lock (Gate)
        {
            Directory.CreateDirectory(directory);
            if (Written.Add(plan.Identity))
            {
                WriteKernel(directory, plan.Layout.Kernel);
                var map = new
                {
                    plan.Identity, plan.Workers, plan.Residents, plan.MaximumDepth, plan.MaximumSteps, plan.Quantum,
                    plan.RootTupleCount, plan.RootBankWords, plan.InputRootWords, plan.ResultRootWords,
                    plan.Program.GraphHash, plan.Program.VerifiedHash, plan.Program.LoweredHash, plan.Program.MapsHash,
                    plan.Program.RequiredServices, plan.Program.EntryProjection,
                    Bodies = plan.Program.Bodies.Select(body => new
                    {
                        body.Function, body.MethodIdentity, body.PrivateWordCount,
                        Blocks = body.SourceBlocks.Select(block => new
                        {
                            block.Block, block.GeneratedBlocks, block.Roots, block.ReturnedRoots,
                            block.Instruction.Offset, block.Instruction.OpCode, block.Instruction.Effects,
                            block.Instruction.Faults, block.Instruction.MemoryType,
                        }),
                    }),
                };
                File.WriteAllText(Path.Combine(directory, plan.Identity + ".source-maps.json"), JsonSerializer.Serialize(map));
            }
            if (Written.Add("runtime-services")) { WriteServices(directory); }
        }
    }

    private static void WriteServices(string directory)
    {
        foreach (Type service in new[] { typeof(WarpPortableSchedulerServices), typeof(WarpPortableHeapServices), typeof(WarpPortableHostPublicationServices) })
        {
            foreach (MethodInfo method in service.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(method =>
                method.ReturnType == typeof(uint) && method.GetParameters().Count(parameter => parameter.ParameterType == typeof(uint[])) == 1))
            {
                WriteKernel(directory, WarpWordArenaServiceLowerer.Lower(method).Kernel);
            }
        }
        WriteKernel(directory, WarpManagedAtomicKernels.Create32()[2].Kernel);
    }

    private static void WriteKernel(string directory, WarpControlFlowKernel kernel)
    {
        string hash = WarpIrHash.Compute(kernel);
        File.WriteAllBytes(Path.Combine(directory, hash + ".word-ir-plan"), WarpCoreCLRBinaryPlanCodec.Serialize(kernel));
    }
}
