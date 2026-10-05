using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;

namespace WarpCLR.CoreCLR.Worker;

internal static partial class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is ["--inherited-pipe-probe"])
            {
                await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
                return 0;
            }
            if (args.Length != 0) { return 2; }
            if (OperatingSystem.IsLinux() && SetProcessGroup(0, 0) != 0) { return 3; }
            using Stream input = Console.OpenStandardInput();
            using Stream output = Console.OpenStandardOutput();
            byte[] bootstrap = new byte[65];
            await input.ReadExactlyAsync(bootstrap).ConfigureAwait(false);
            byte[] key = bootstrap[..32], deployment = WarpCoreCLRWorkerIdentity.DeploymentDigest(AppContext.BaseDirectory);
            if (!CryptographicOperations.FixedTimeEquals(bootstrap.AsSpan(32, 32), deployment)) { return 4; }
            await WarpCoreCLRWorkerProtocol.WriteAsync(output, key, WarpCoreCLRWorkerProtocol.Hello, 0,
                WarpCoreCLRWorkerIdentity.Hello(deployment, typeof(Program).Assembly.ManifestModule.ModuleVersionId,
                    Environment.ProcessPath ?? throw new PlatformNotSupportedException("The actual native dotnet host identity is unavailable.")), CancellationToken.None).ConfigureAwait(false);
            if (bootstrap[64] == 3)
            {
                await ExitLeaderWithInheritedPipeAsync(input, output, key).ConfigureAwait(false);
                return 0;
            }
            CoreCLRResumableKernel kernel = await CompileAsync(input, output, key, bootstrap[64]).ConfigureAwait(false);
            await ExecuteAsync(input, output, key, kernel).ConfigureAwait(false);
            return 0;
        }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or OverflowException or
            NotSupportedException or CoreCLRCompilationResourceException or CoreCLRResourceLimitException or OutOfMemoryException)
        {
            await Console.Error.WriteLineAsync(error.GetType().Name).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<CoreCLRResumableKernel> CompileAsync(Stream input, Stream output, byte[] key, byte probe)
    {
        WarpCoreCLRWorkerProtocol.Frame frame = await WarpCoreCLRWorkerProtocol.ReadAsync(input, key, 1, CancellationToken.None).ConfigureAwait(false);
        if (frame.Kind != WarpCoreCLRWorkerProtocol.Compile || frame.Payload.Length < 64) { throw new InvalidDataException("Expected one compiled plan."); }
        string irHash = Encoding.ASCII.GetString(frame.Payload, 0, 64);
        var layout = new WarpLogicalMachineLayout(WarpCoreCLRBinaryPlanCodec.Deserialize(frame.Payload.AsSpan(64), irHash));
        await WarpCoreCLRWorkerProtocol.WriteAsync(output, key, 8, 1, [], CancellationToken.None).ConfigureAwait(false);
        if (probe == 1) { await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false); }
        if (probe == 2) { await StartInheritedPipeProbeAsync(output, key).ConfigureAwait(false); await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false); }
        if (probe > 2) { throw new InvalidDataException("Unknown worker admission probe."); }
        MethodInfo method = CoreCLRResumableKernel.EmitCompilation(layout);
        await WarpCoreCLRWorkerProtocol.WriteAsync(output, key, WarpCoreCLRWorkerProtocol.JitEntering, 1,
            method.Module.ModuleVersionId.ToByteArray(), CancellationToken.None).ConfigureAwait(false);
        CoreCLRResumableKernel kernel = CoreCLRResumableKernel.PrepareCompilation(layout, method);
        byte[] identity = new byte[17];
        method.Module.ModuleVersionId.ToByteArray().CopyTo(identity, 0); identity[16] = kernel.IsCollectible ? (byte)1 : (byte)0;
        await WarpCoreCLRWorkerProtocol.WriteAsync(output, key, WarpCoreCLRWorkerProtocol.Compiled, 1,
            identity, CancellationToken.None).ConfigureAwait(false);
        return kernel;
    }

    private static async Task ExecuteAsync(Stream input, Stream output, byte[] key, CoreCLRResumableKernel kernel)
    {
        var bindings = new InputBindings();
        for (ulong sequence = 2; sequence != 0; sequence++)
        {
            WarpCoreCLRWorkerProtocol.Frame frame = await WarpCoreCLRWorkerProtocol.ReadAsync(input, key, sequence, CancellationToken.None).ConfigureAwait(false);
            if (frame.Kind != WarpCoreCLRWorkerProtocol.Execute)
            { await ExecuteBoundCommandAsync(output, key, sequence, frame, kernel, bindings).ConfigureAwait(false); continue; }
            WarpCoreCLRWorkerWords.Invocation call = WarpCoreCLRWorkerWords.ReadRequest(frame.Payload);
            kernel.ExecuteManagedQuantum(call.Inputs, call.Scalars, call.Worker, call.State, call.Depth, call.Quantum, call.Arena, CancellationToken.None);
            await WarpCoreCLRWorkerProtocol.WriteAsync(output, key, WarpCoreCLRWorkerProtocol.Executed, sequence,
                WarpCoreCLRWorkerWords.Response(frame.Payload, call.State, call.Arena), CancellationToken.None).ConfigureAwait(false);
        }
        throw new InvalidDataException("Worker transaction sequence exhausted.");
    }

    private static async Task StartInheritedPipeProbeAsync(Stream output, byte[] key)
    {
        string host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet"));
        var start = new ProcessStartInfo(host) { UseShellExecute = false };
        start.ArgumentList.Add(typeof(Program).Assembly.Location); start.ArgumentList.Add("--inherited-pipe-probe");
        using Process child = Process.Start(start) ?? throw new IOException("Probe child could not start.");
        await WarpCoreCLRWorkerProtocol.WriteAsync(output, key, 7, 1, BitConverter.GetBytes(child.Id), CancellationToken.None).ConfigureAwait(false);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "setpgid", SetLastError = true)]
    private static partial int SetProcessGroup(int processId, int groupId);
}
