using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest creates this internal fixture through reflected discovery.")]
internal sealed class ZProcStatCensusTests
{
    [TestMethod]
    public async Task OpenedStatAfterActualHeldIdentityExitIsAbsentWithoutLosingTerminalAuthority()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("This witness uses actual Linux procfs and held pidfds."); }
        int parentGroup = WarpCoreCLRWorkerContainment.GetProcessGroup(Environment.ProcessId);
        var start = new ProcessStartInfo("/bin/sleep") { UseShellExecute = false };
        start.ArgumentList.Add("100");
        using Process child = Process.Start(start) ?? throw new IOException("The isolated procfs child did not start.");
        try
        {
            using WarpCoreCLRWorkerPidFd identity = WarpCoreCLRWorkerContainment.OpenPidFd(child.Id);
            using var raw = new FileStream($"/proc/{child.Id}/stat", FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var admitted = new FileStream($"/proc/{child.Id}/stat", FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            child.Kill(); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.IsTrue(identity.ObserveStopped());
            IOException error = Assert.ThrowsExactly<IOException>(() => raw.Read(new byte[4096]));
            Assert.AreEqual(3, error.HResult);
            (char state, int group) = WarpCoreCLRWorkerGroupCensus.ReadStatRecord(admitted);
            Assert.AreEqual('\0', state); Assert.AreEqual(0, group);
            Assert.IsTrue(identity.ObserveStopped());
            Assert.AreEqual(parentGroup, WarpCoreCLRWorkerContainment.GetProcessGroup(Environment.ProcessId));
        }
        finally
        {
            if (!child.HasExited) { child.Kill(); }
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [TestMethod]
    [DataRow(5)]
    [DataRow(13)]
    [DataRow(22)]
    [DataRow(-2146232800)]
    public void UnrelatedStatIoFailuresRemainFaults(int errno)
    {
        using var stream = new FaultingStatStream(errno);
        IOException error = Assert.ThrowsExactly<IOException>(() => WarpCoreCLRWorkerGroupCensus.ReadStatRecord(stream));
        Assert.AreEqual(errno, error.HResult);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void EmptyMalformedAndOversizedRecordsRemainRejected(int kind)
    {
        byte[] bytes = kind switch { 0 => [], 1 => Encoding.UTF8.GetBytes("1 (truncated) R"), _ => new byte[4096] };
        using var stream = new MemoryStream(bytes);
        Assert.ThrowsExactly<IOException>(() => WarpCoreCLRWorkerGroupCensus.ReadStatRecord(stream));
    }

    [TestMethod]
    [DataRow('R')]
    [DataRow('S')]
    [DataRow('Z')]
    [DataRow('X')]
    public void ExactKernelMinusOneGroupSuppliesNoPrivateGroupOrTerminalAuthority(char state)
    {
        // do_task_stat samples state before acquiring the possibly removed
        // sighand, so the -1 group sentinel is not limited to X/Z snapshots.
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"123 (worker)) {state} 0 -1 -1 0 0\n"));
        (char observedState, int group) = WarpCoreCLRWorkerGroupCensus.ReadStatRecord(stream);
        Assert.AreEqual(state, observedState);
        Assert.AreEqual(-1, group);
        Assert.AreNotEqual(WarpCoreCLRWorkerContainment.GetProcessGroup(Environment.ProcessId), group);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(7)]
    public void FragmentedActualKernelStatRetainsItsCompleteExactGroup(int chunk)
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("This witness reads the actual Linux procfs record."); }
        byte[] record = File.ReadAllBytes($"/proc/{Environment.ProcessId}/stat");
        using var stream = new FragmentedStatStream(record, chunk);
        (_, int group) = WarpCoreCLRWorkerGroupCensus.ReadStatRecord(stream);
        Assert.AreEqual(WarpCoreCLRWorkerContainment.GetProcessGroup(Environment.ProcessId), group);
    }

    [TestMethod]
    [DataRow("-2")]
    [DataRow("+1")]
    [DataRow("-01")]
    [DataRow("2147483648")]
    [DataRow("--1")]
    [DataRow("no-group")]
    public void OtherSignedOverflowAndMalformedGroupsRemainRejected(string group)
    {
        using var stream = new FragmentedStatStream(Encoding.UTF8.GetBytes($"123 (worker) R 0 {group} 0\n"), 2);
        Assert.ThrowsExactly<IOException>(() => WarpCoreCLRWorkerGroupCensus.ReadStatRecord(stream));
    }

    private sealed class FragmentedStatStream(byte[] record, int chunk) : MemoryStream(record)
    {
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, chunk)]);
    }

    private sealed class FaultingStatStream(int errno) : MemoryStream
    {
        public override int Read(Span<byte> buffer) => throw new IOException("Unrelated census I/O failure.", errno);
    }
}
