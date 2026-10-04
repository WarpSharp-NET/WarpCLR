namespace WarpCLR.IR;

internal sealed class WarpWideReturnTerminator : WarpTupleReturnTerminator
{
    public WarpWideReturnTerminator(int low, int high) : base([low, high])
    {
    }

    public int Low => Values[0];

    public int High => Values[1];
}
