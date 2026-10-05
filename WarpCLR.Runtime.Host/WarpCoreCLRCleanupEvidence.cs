namespace WarpCLR.Runtime.Host;

internal sealed record WarpCoreCLRCleanupEvidence(int ProcessId, string Milestone, long Timestamp, int ThreadId, bool ThreadPool);
