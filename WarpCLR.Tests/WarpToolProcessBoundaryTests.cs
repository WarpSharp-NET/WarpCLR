using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using WarpCLR.Runtime.Host;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class WarpToolProcessBoundaryTests
{
    private static readonly string[] TimeoutArguments = ["-c", "printf 'compiler stdout before timeout\\n'; printf 'compiler stderr before timeout\\n' >&2; exec /bin/sleep 30"];
    private static readonly string[] FailureArguments = ["-c", "printf 'compiler stdout before failure\\n'; printf 'compiler stderr before failure\\n' >&2; exit 17"];
    private static readonly string[] SuccessArguments = ["-c", "printf 'compiler stdout\\n'; printf 'compiler stderr\\n' >&2"];

    [TestMethod]
    public async Task TimeoutRetainsBothDiagnosticStreamsAndBoundedCleanupFacts()
    {
        RequireLinux();
        var clock = Stopwatch.StartNew();
        WarpHostException error = await Assert.ThrowsExactlyAsync<WarpHostException>(() => WarpToolProcess.RunAsync(
            "/bin/sh", TimeoutArguments, null, TimeSpan.FromMilliseconds(500))).ConfigureAwait(false);
        Assert.AreEqual("WRPNATIVE2004", error.Code, StringComparer.Ordinal);
        AssertDiagnosticPrefix(error, "timeout");
        Assert.IsGreaterThan(0, (int)error.Data["ProcessId"]!);
        Assert.IsGreaterThan(TimeSpan.Zero, (TimeSpan)error.Data["Elapsed"]!);
        Assert.IsTrue((bool)error.Data["RootExitObserved"]!);
        Assert.IsTrue((bool)error.Data["ReadersStopped"]!);
        Assert.IsLessThan(TimeSpan.FromSeconds(8), clock.Elapsed);
    }

    [TestMethod]
    public async Task ExplicitCancellationKeepsItsTokenAndDiagnosticPrefix()
    {
        RequireLinux();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        OperationCanceledException error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => WarpToolProcess.RunAsync(
            "/bin/sh", TimeoutArguments, null, TimeSpan.FromSeconds(30), cancellation.Token)).ConfigureAwait(false);
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        AssertDiagnosticPrefix(error, "timeout");
        Assert.IsTrue((bool)error.Data["ReadersStopped"]!);
    }

    [TestMethod]
    public async Task AggregatePipelineDeadlinePreservesCompilerFailureEvidence()
    {
        RequireLinux();
        WarpHostException error = await Assert.ThrowsExactlyAsync<WarpHostException>(() => WarpNativeCompilationDeadline.RunAsync(
            TimeSpan.FromMilliseconds(500), token => WarpToolProcess.RunAsync("/bin/sh", TimeoutArguments, null, TimeSpan.FromSeconds(30), token))).ConfigureAwait(false);
        Assert.AreEqual("WRPNATIVE2004", error.Code, StringComparer.Ordinal);
        AssertDiagnosticPrefix(error, "timeout");
        Assert.IsGreaterThan(0, (int)error.Data["ProcessId"]!);
        Assert.IsTrue((bool)error.Data["ReadersStopped"]!);
    }

    [TestMethod]
    public async Task PipelineCallerCancellationKeepsItsOriginalTokenAndEvidence()
    {
        RequireLinux();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        OperationCanceledException error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => WarpNativeCompilationDeadline.RunAsync(
            TimeSpan.FromSeconds(30), token => WarpToolProcess.RunAsync("/bin/sh", TimeoutArguments, null, TimeSpan.FromSeconds(30), token), cancellation.Token)).ConfigureAwait(false);
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        AssertDiagnosticPrefix(error, "timeout");
        Assert.IsTrue((bool)error.Data["ReadersStopped"]!);
    }

    [TestMethod]
    public async Task NonzeroExitRetainsProcessIdentityTimingAndExitCode()
    {
        RequireLinux();
        WarpHostException error = await Assert.ThrowsExactlyAsync<WarpHostException>(() => WarpToolProcess.RunAsync(
            "/bin/sh", FailureArguments, null, TimeSpan.FromSeconds(10))).ConfigureAwait(false);
        Assert.AreEqual("WRPNATIVE2002", error.Code, StringComparer.Ordinal);
        AssertDiagnosticPrefix(error, "failure");
        Assert.AreEqual(17, error.Data["ExitCode"]);
        Assert.IsGreaterThan(0, (int)error.Data["ProcessId"]!);
        Assert.IsGreaterThan(TimeSpan.Zero, (TimeSpan)error.Data["Elapsed"]!);
        Assert.IsTrue((bool)error.Data["StandardOutputEndOfStream"]!);
        Assert.IsTrue((bool)error.Data["StandardErrorEndOfStream"]!);
    }

    [TestMethod]
    public async Task ParentExitDoesNotRemoveTheDeadlineForInheritedPipes()
    {
        RequireLinux();
        string directory = Directory.CreateTempSubdirectory("warpclr-tool-orphan-proof-").FullName;
        string release = Path.Combine(directory, "release");
        string ready = Path.Combine(directory, "ready");
        string exited = Path.Combine(directory, "exited");
        string script = Path.Combine(directory, "child.sh");
        await File.WriteAllTextAsync(script,
            "printf 'descendant stdout\\n'\nprintf 'descendant stderr\\n' >&2\nprintf '%s' \"$$\" > \"$2\"\n" +
            "while [ ! -f \"$1\" ]; do /bin/sleep 0.01; done\nprintf 'released' > \"$3\"\n").ConfigureAwait(false);
        string[] arguments = ["-c", "printf 'parent stdout\\n'; printf 'parent stderr\\n' >&2; /bin/sh \"$1\" \"$2\" \"$3\" \"$4\" & while [ ! -f \"$3\" ]; do /bin/sleep 0.01; done; exit 0", "probe", script, release, ready, exited];
        Task<WarpToolProcessResult> operation = WarpToolProcess.RunAsync("/bin/sh", arguments, directory, TimeSpan.FromSeconds(1));
        var clock = Stopwatch.StartNew();
        try
        {
            await WaitForFileAsync(ready).ConfigureAwait(false);
            string identity = await File.ReadAllTextAsync(ready).ConfigureAwait(false);
            int descendant = int.Parse(identity, NumberStyles.None, CultureInfo.InvariantCulture);
            Assert.IsGreaterThan(0, descendant);
            WarpHostException error = await Assert.ThrowsExactlyAsync<WarpHostException>(() => operation.WaitAsync(TimeSpan.FromSeconds(8))).ConfigureAwait(false);
            Assert.AreEqual("WRPNATIVE2004", error.Code, StringComparer.Ordinal);
            StringAssert.Contains(error.Message, "parent stdout", StringComparison.Ordinal);
            StringAssert.Contains(error.Message, "parent stderr", StringComparison.Ordinal);
            StringAssert.Contains(error.Message, "descendant stdout", StringComparison.Ordinal);
            StringAssert.Contains(error.Message, "descendant stderr", StringComparison.Ordinal);
            Assert.IsTrue((bool)error.Data["RootExitObserved"]!);
            Assert.IsTrue((bool)error.Data["ReadersStopped"]!);
            Assert.IsFalse((bool)error.Data["StandardOutputEndOfStream"]!);
            Assert.IsFalse((bool)error.Data["StandardErrorEndOfStream"]!);
            Assert.IsTrue((bool)error.Data["CleanupIncomplete"]!);
            StringAssert.Contains((string)error.Data["Cleanup"]!, "unverified", StringComparison.Ordinal);
            Assert.IsFalse(File.Exists(exited), "The orphan lease, rather than the exited parent, still owns recovery.");
            TestContext.WriteLine($"Inherited-pipe deadline: {clock.Elapsed}; acknowledged private descendant: {descendant}.");
        }
        finally
        {
            // This marker releases only the acknowledged probe in our privately owned directory.
            await File.WriteAllTextAsync(release, "release").ConfigureAwait(false);
            await WaitForFileAsync(exited).ConfigureAwait(false);
            try { await operation.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false); }
            catch (WarpHostException) { }
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task SuccessfulCompilerKeepsExactOutputFromBothStreams()
    {
        RequireLinux();
        WarpToolProcessResult result = await WarpToolProcess.RunAsync(
            "/bin/sh", SuccessArguments, null, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual("compiler stdout\n", result.StandardOutput, StringComparer.Ordinal);
        Assert.AreEqual("compiler stderr\n", result.StandardError, StringComparer.Ordinal);
    }

    public TestContext TestContext { get; set; } = null!;

    private static void AssertDiagnosticPrefix(Exception error, string suffix)
    {
        StringAssert.Contains(error.Message, "compiler stdout before " + suffix, StringComparison.Ordinal);
        StringAssert.Contains(error.Message, "compiler stderr before " + suffix, StringComparison.Ordinal);
    }

    private static void RequireLinux()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("The real compiler-process probes require Linux shell tools."); }
    }

    private static async Task WaitForFileAsync(string path)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(path)) { await Task.Delay(TimeSpan.FromMilliseconds(10), deadline.Token).ConfigureAwait(false); }
    }
}
