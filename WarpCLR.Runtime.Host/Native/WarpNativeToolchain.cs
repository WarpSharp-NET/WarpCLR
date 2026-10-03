using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host.Native;

internal sealed class WarpNativeToolchain
{
    private readonly WarpNativeToolchainOptions options;

    public WarpNativeToolchain(WarpNativeToolchainOptions? options = null)
    {
        this.options = options ?? new WarpNativeToolchainOptions();
        if (this.options.ProcessTimeout <= TimeSpan.Zero || this.options.ProcessTimeout > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The toolchain timeout must be finite and positive.");
        }
        if (this.options.MaximumSourceBytes <= 0 || this.options.MaximumImageBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Native compilation source/image admission limits must be positive.");
        }
    }

    public async Task<WarpNativeImage> CompileAsync(
        WarpBackendArtifact artifact,
        WarpNativeTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(target);
        if (artifact.Backend != target.Backend)
        {
            throw new WarpHostException("WRPNATIVE1003", "The artifact and concrete target select different backends.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (artifact.Content.Length > options.MaximumSourceBytes)
        {
            throw new WarpHostException("WRPNATIVE1005", "The native source artifact exceeds the compilation admission limit.");
        }
        string sourceText = artifact.GetText();
        string parameterPrefix = target.Backend == WarpBackendKind.NVPTX ? @"\.param \.u(?:64|32) " : @"(?:ptr addrspace\(1\)|i32) %";
        int inputCount = Regex.Count(sourceText, parameterPrefix + @"warp_input_[0-9]+", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));
        int scalarCount = Regex.Count(sourceText, parameterPrefix + @"warp_scalar_[0-9]+", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));
        if (target.Backend == WarpBackendKind.NVPTX)
        {
            if (Regex.Count(sourceText, @"(?m)^\.target sm_[0-9]{2,3}\r?$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1)) != 1)
            {
                throw new WarpHostException("WRPNATIVE2003", "The PTX artifact must declare exactly one concrete architecture.");
            }

            string ptx = Regex.Replace(sourceText, @"(?m)^\.target sm_[0-9]{2,3}\r?$",
                ".target " + target.Architecture, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));
            if (!ptx.Contains(".target " + target.Architecture + "\n", StringComparison.Ordinal) &&
                !ptx.Contains(".target " + target.Architecture + "\r\n", StringComparison.Ordinal))
            {
                throw new WarpHostException("WRPNATIVE2003", "The PTX artifact does not have one retargetable architecture declaration.");
            }

            return new WarpNativeImage(target, WarpNativeImageFormat.Ptx, artifact.EntryPoint,
                Encoding.UTF8.GetBytes(ptx), artifact.ContentHash, "cuda-driver-jit/" + target.RuntimeIdentity, inputCount, scalarCount);
        }

        if (target.Backend == WarpBackendKind.SPIRV)
        {
            // Khronos' translator consumes SPIR LLVM, not LLVM's distinct SPIR-V backend triple.
            sourceText = sourceText.Replace("target triple = \"spirv64-unknown-unknown\"",
                "target triple = \"spir64-unknown-unknown\"", StringComparison.Ordinal);
        }

        return await CompileLlvmAsync(sourceText, artifact.EntryPoint, artifact.ContentHash, target,
            inputCount, scalarCount, null, cancellationToken).ConfigureAwait(false);
    }

    public Task<WarpNativeImage> CompileMachineAsync(
        WarpLogicalMachineLayout layout,
        WarpNativeTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        string source = new WarpPortableMachineEmitter().Emit(layout, target.Backend);
        string sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        return CompileLlvmAsync(source, WarpPortableMachineEmitter.EntryPoint, sourceHash, target,
            layout.Kernel.InputBufferCount, layout.Kernel.ScalarArgumentCount, layout, cancellationToken);
    }

    private async Task<WarpNativeImage> CompileLlvmAsync(
        string llvmText,
        string entryPoint,
        string sourceHash,
        WarpNativeTarget target,
        int inputCount,
        int scalarCount,
        WarpLogicalMachineLayout? machineLayout,
        CancellationToken cancellationToken)
    {
        if (Encoding.UTF8.GetByteCount(llvmText) > options.MaximumSourceBytes)
        {
            throw new WarpHostException("WRPNATIVE1005", "The native source exceeds the compilation admission limit.");
        }

        string directory = Directory.CreateTempSubdirectory("warpclr-native-").FullName;
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            string source = Path.Combine(directory, "module.ll");
            string bitcode = Path.Combine(directory, "module.bc");
            await File.WriteAllTextAsync(source, llvmText, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            string assembler = await VersionAsync(options.LlvmAssembler, cancellationToken).ConfigureAwait(false);
            await RunAsync(options.LlvmAssembler, [source, "-o", bitcode], directory, cancellationToken).ConfigureAwait(false);

            (string imagePath, string toolchain, WarpNativeImageFormat format) = target.Backend switch
            {
                WarpBackendKind.NVPTX => await CompilePtxAsync(target, assembler, bitcode, directory, cancellationToken).ConfigureAwait(false),
                WarpBackendKind.AMDGPU => await CompileHsacoAsync(target, assembler, bitcode, directory, cancellationToken).ConfigureAwait(false),
                _ => await CompileSpirVAsync(assembler, bitcode, directory, cancellationToken).ConfigureAwait(false),
            };

            if (new FileInfo(imagePath).Length > options.MaximumImageBytes)
            {
                throw new WarpHostException("WRPNATIVE1005", "The native image exceeds the compilation admission limit.");
            }

            byte[] content = await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(false);
            return new WarpNativeImage(target, format, entryPoint, content, sourceHash,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(toolchain))), inputCount, scalarCount, machineLayout);
        }
        finally
        {
            // Only this randomly-created, privately owned compiler scratch directory is removed.
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task<(string ImagePath, string Toolchain, WarpNativeImageFormat Format)> CompilePtxAsync(
        WarpNativeTarget target, string assembler, string bitcode, string directory, CancellationToken cancellationToken)
    {
        string codeGenerator = await VersionAsync(options.LlvmCodeGenerator, cancellationToken).ConfigureAwait(false);
        string imagePath = Path.Combine(directory, "module.ptx");
        await RunAsync(options.LlvmCodeGenerator,
            ["-mtriple=nvptx64-nvidia-cuda", "-mcpu=" + target.Architecture, "-mattr=+ptx80",
             "-filetype=asm", "-O=2", bitcode, "-o", imagePath], directory, cancellationToken).ConfigureAwait(false);
        return (imagePath, assembler + "\n" + codeGenerator + "\n" + target.RuntimeIdentity, WarpNativeImageFormat.Ptx);
    }

    private async Task<(string ImagePath, string Toolchain, WarpNativeImageFormat Format)> CompileHsacoAsync(
        WarpNativeTarget target, string assembler, string bitcode, string directory, CancellationToken cancellationToken)
    {
        string codeGenerator = await VersionAsync(options.LlvmCodeGenerator, cancellationToken).ConfigureAwait(false);
        string linker = await VersionAsync(options.LlvmLinker, cancellationToken).ConfigureAwait(false);
        string objectFile = Path.Combine(directory, "module.o");
        string imagePath = Path.Combine(directory, "module.hsaco");
        string[] parts = target.Architecture.Split(':');
        var arguments = new List<string>
        {
            "-mtriple=amdgcn-amd-amdhsa", "-mcpu=" + parts[0], "-filetype=obj", "-O=2",
            bitcode, "-o", objectFile,
        };
        if (parts.Length > 1)
        {
            arguments.Add("-mattr=" + string.Join(',', parts.Skip(1).Select(part => part[^1] + part[..^1])));
        }

        await RunAsync(options.LlvmCodeGenerator, arguments, directory, cancellationToken).ConfigureAwait(false);
        await RunAsync(options.LlvmLinker, ["-shared", "--no-undefined", objectFile, "-o", imagePath],
            directory, cancellationToken).ConfigureAwait(false);
        return (imagePath, assembler + "\n" + codeGenerator + "\n" + linker, WarpNativeImageFormat.Hsaco);
    }

    private async Task<(string ImagePath, string Toolchain, WarpNativeImageFormat Format)> CompileSpirVAsync(
        string assembler, string bitcode, string directory, CancellationToken cancellationToken)
    {
        string translator = await VersionAsync(options.SpirVTranslator, cancellationToken).ConfigureAwait(false);
        string validator = await VersionAsync(options.SpirVValidator, cancellationToken).ConfigureAwait(false);
        string imagePath = Path.Combine(directory, "module.spv");
        await RunAsync(options.SpirVTranslator, ["--spirv-max-version=1.2", bitcode, "-o", imagePath],
            directory, cancellationToken).ConfigureAwait(false);
        await RunAsync(options.SpirVValidator, ["--target-env", "opencl2.2", imagePath], directory, cancellationToken).ConfigureAwait(false);
        return (imagePath, assembler + "\n" + translator + "\n" + validator, WarpNativeImageFormat.SpirV);
    }

    private async Task<string> VersionAsync(string tool, CancellationToken cancellationToken)
    {
        WarpToolProcessResult result = await RunAsync(tool, ["--version"], null, cancellationToken).ConfigureAwait(false);
        return tool + "\n" + result.StandardOutput + "\n" + result.StandardError;
    }

    private Task<WarpToolProcessResult> RunAsync(string tool, IReadOnlyList<string> arguments,
        string? directory, CancellationToken cancellationToken) =>
        WarpToolProcess.RunAsync(tool, arguments, directory, options.ProcessTimeout, cancellationToken);
}
