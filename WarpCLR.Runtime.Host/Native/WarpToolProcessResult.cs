namespace WarpCLR.Runtime.Host.Native;

internal sealed record WarpToolProcessResult(int ExitCode, string StandardOutput, string StandardError);
