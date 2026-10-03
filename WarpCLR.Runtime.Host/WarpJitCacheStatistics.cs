using System.Runtime.InteropServices;

namespace WarpCLR.Runtime.Host;

[StructLayout(LayoutKind.Auto)]
public readonly record struct WarpJitCacheStatistics(long CompilationCount, long MemoryHitCount, long DiskHitCount, int MemoryEntryCount);
