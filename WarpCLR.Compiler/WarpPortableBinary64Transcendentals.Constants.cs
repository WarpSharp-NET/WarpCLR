namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64Transcendentals
{

    private static uint ConstantLow(uint kind) => ConstantLow0_12(kind);

    private static uint ConstantLow0_12(uint index)
    {
        if (index == 0) { return 0x00000000u; }
        if (index == 1) { return 0x54442D18u; }
        if (index == 2) { return 0x54442D18u; }
        if (index == 3) { return 0x54442D18u; }
        if (index == 4) { return 0xFEE00000u; }
        if (index == 5) { return 0x35793C76u; }
        if (index == 6) { return 0x652B82FEu; }
        if (index == 7) { return 0x1526E50Eu; }
        if (index == 8) { return 0x667F3BCDu; }
        if (index == 9) { return 0x99FCEF32u; }
        if (index == 10) { return 0x55555555u; }
        return 0xFEFA39EFu;
    }

    private static uint ConstantHigh(uint kind) => ConstantHigh0_12(kind);

    private static uint ConstantHigh0_12(uint index)
    {
        if (index == 0) { return 0x3FF00000u; }
        if (index == 1) { return 0x400921FBu; }
        if (index == 2) { return 0x3FF921FBu; }
        if (index == 3) { return 0x3FE921FBu; }
        if (index == 4) { return 0x3FE62E42u; }
        if (index == 5) { return 0x3DEA39EFu; }
        if (index == 6) { return 0x3FF71547u; }
        if (index == 7) { return 0x3FDBCB7Bu; }
        if (index == 8) { return 0x3FF6A09Eu; }
        if (index == 9) { return 0x3FDA8279u; }
        if (index == 10) { return 0x3FD55555u; }
        return 0x3FE62E42u;
    }

}
