using System.Globalization;
using System.Text;

namespace WarpCLR.Runtime.Host;

internal static class WarpCoreCLRWorkerGroupCensus
{
    private const int MaximumVisibleProcesses = 65536;
    private const int MaximumStatBytes = 4096;

    internal static void ValidateProcDomain(int process, int group)
    {
        (char state, int observed) = ReadStat(process);
        if (state == '\0' || observed != group)
        { throw new PlatformNotSupportedException("The bounded Linux worker requires its exact PID namespace to be visible through /proc."); }
    }

    internal static void WaitStopped(int group, WarpCoreCLRWorkerCleanupAttempt attempt)
    {
        while (attempt.Remaining > TimeSpan.Zero)
        {
            if (!HasActiveMember(group, attempt)) { return; }
            Thread.Sleep(1);
        }
        throw new TimeoutException("The signalled private CoreCLR group did not become terminal within the shared cleanup quota.");
    }

    internal static void ValidateDeclared(int group, HashSet<int> declared, WarpCoreCLRWorkerCleanupAttempt attempt)
    {
        int count = 0;
        using IEnumerator<string> directories = Directory.EnumerateDirectories("/proc").GetEnumerator();
        while (true)
        {
            RequireRemaining(attempt);
            if (!directories.MoveNext()) { break; }
            string directory = directories.Current;
            if (!int.TryParse(Path.GetFileName(directory), NumberStyles.None, CultureInfo.InvariantCulture, out int process)) { continue; }
            RequireCount(++count);
            RequireRemaining(attempt);
            (_, int observed) = ReadStat(process);
            if (observed == group && !declared.Contains(process))
            { throw new InvalidDataException("The private worker group contains an undeclared process without an authenticated held identity."); }
        }
        RequireRemaining(attempt);
    }

    private static bool HasActiveMember(int group, WarpCoreCLRWorkerCleanupAttempt attempt)
    {
        int count = 0;
        using IEnumerator<string> directories = Directory.EnumerateDirectories("/proc").GetEnumerator();
        while (true)
        {
            RequireRemaining(attempt);
            if (!directories.MoveNext()) { break; }
            string directory = directories.Current;
            if (!int.TryParse(Path.GetFileName(directory), NumberStyles.None, CultureInfo.InvariantCulture, out int process)) { continue; }
            RequireCount(++count);
            RequireRemaining(attempt);
            (char state, int observed) = ReadStat(process);
            if (observed == group && state is not '\0' and not 'Z' and not 'X' and not 'x') { return true; }
        }
        return false;
    }

    private static void RequireCount(int count)
    {
        if (count > MaximumVisibleProcesses) { throw new IOException("The bounded worker process census exceeded its admitted namespace size."); }
    }

    private static void RequireRemaining(WarpCoreCLRWorkerCleanupAttempt attempt)
    {
        if (attempt.Remaining <= TimeSpan.Zero) { throw new TimeoutException("The process census cannot enumerate after the shared cleanup quota."); }
    }

    private static (char State, int Group) ReadStat(int process)
    {
        try
        {
            using var stream = new FileStream($"/proc/{process}/stat", FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return ReadStatRecord(stream);
        }
        catch (IOException error) when (IsAbsentStat(error)) { return ('\0', 0); }
    }

    internal static (char State, int Group) ReadStatRecord(Stream stream)
    {
        try
        {
            Span<byte> bytes = stackalloc byte[MaximumStatBytes];
            int length = ReadBoundedRecord(stream, bytes);
            string stat = Encoding.UTF8.GetString(bytes[..length]);
            int end = stat.LastIndexOf(')');
            if (end < 0) { throw new IOException("The process census stat record has no command boundary."); }
            string[] fields = stat[(end + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 3 || fields[0].Length != 1 ||
                !TryReadGroup(fields[2], out int group))
            { throw new IOException("The process census stat identity is malformed."); }
            return (fields[0][0], group);
        }
        // A task can disappear after open. Linux proc_single_show then returns
        // ESRCH; pinned .NET preserves the raw Unix errno in IOException.HResult.
        // This supplements the census only. Held pidfds still prove every stop.
        catch (IOException error) when (IsAbsentStat(error)) { return ('\0', 0); }
    }

    private static int ReadBoundedRecord(Stream stream, Span<byte> bytes)
    {
        int length = 0;
        while (length < bytes.Length)
        {
            int read = stream.Read(bytes[length..]);
            if (read == 0) { break; }
            length = checked(length + read);
        }
        if (length == 0 || length == bytes.Length)
        { throw new IOException("The process census stat record exceeded admission or was empty."); }
        return length;
    }

    private static bool TryReadGroup(string value, out int group)
    {
        // Linux do_task_stat leaves pgid=-1 when exit has removed sighand.
        // Preserve that exact sentinel: it matches no positive private group
        // and supplies no stopped authority; held pidfds still prove every stop.
        if (string.Equals(value, "-1", StringComparison.Ordinal)) { group = -1; return true; }
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out group);
    }

    private static bool IsAbsentStat(IOException error) => error is FileNotFoundException or DirectoryNotFoundException ||
        OperatingSystem.IsLinux() && error.HResult == 3;
}
