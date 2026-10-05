namespace WarpCLR.Runtime.Host;

internal sealed record WarpCompiledPausedWorker(uint Worker, uint State, uint RunGeneration, uint Physical,
    uint Quanta, WarpCompiledWorkerTicket? Ticket, bool Executed, uint[] SourceState,
    WarpCompiledServiceContinuation? Helper);
