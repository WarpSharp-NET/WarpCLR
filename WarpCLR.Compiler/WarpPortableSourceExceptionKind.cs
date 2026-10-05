namespace WarpCLR.Compiler;

internal static class WarpPortableSourceExceptionKind
{
    internal const uint None = 0;
    internal const uint Exception = 1;
    internal const uint System = 2;
    internal const uint NullReference = 3;
    internal const uint IndexOutOfRange = 4;
    internal const uint Overflow = 5;
    internal const uint DivideByZero = 6;
    internal const uint Arithmetic = 7;
    internal const uint ArrayTypeMismatch = 8;
    internal const uint InvalidCast = 9;
    internal const uint InvalidOperation = 10;
    internal const uint OutOfMemory = 11;
    internal const uint StackOverflow = 12;
    internal const uint Argument = 13;
    internal const uint ArgumentOutOfRange = 14;
    internal const uint ArgumentNull = 15;
    internal const uint TypeInitialization = 16;
}
