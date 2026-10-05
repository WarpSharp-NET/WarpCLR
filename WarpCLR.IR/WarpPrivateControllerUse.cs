namespace WarpCLR.IR;

internal sealed record WarpPrivateControllerUse(int Function, int Block, int Value,
    int Callee, int Argument, int CallValue, string ServiceIdentity);
