namespace WarpCLR.Compiler;

internal sealed record WarpPortableSourceFrameView(uint ByteOffset, uint ByteSpan, uint ElementType,
    string Storage, int Slot);
