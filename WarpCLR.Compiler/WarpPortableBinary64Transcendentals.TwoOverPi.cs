namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64Transcendentals
{
    private static uint TwoOverPi(uint index) => index >= 64 ? 0 : TwoOverPi0_64(index);

    private static uint TwoOverPi0_16(uint index)
    {
        if (index == 0) { return 0xCAF27F1Du; }
        if (index == 1) { return 0x9F3A1F35u; }
        if (index == 2) { return 0x6B1E5EF8u; }
        if (index == 3) { return 0xC33D26EFu; }
        if (index == 4) { return 0x98327DBBu; }
        if (index == 5) { return 0x32C2DE4Fu; }
        if (index == 6) { return 0x3F7E33E8u; }
        if (index == 7) { return 0xA5FF0705u; }
        if (index == 8) { return 0x5719053Eu; }
        if (index == 9) { return 0xDDAF44D1u; }
        if (index == 10) { return 0x8B961CA6u; }
        if (index == 11) { return 0x8359C476u; }
        if (index == 12) { return 0xDCE8092Au; }
        if (index == 13) { return 0x19C367CDu; }
        if (index == 14) { return 0x8C6B47C4u; }
        return 0x60E27BC0u;
    }

    private static uint TwoOverPi16_32(uint index)
    {
        if (index == 16) { return 0xCA73A8C9u; }
        if (index == 17) { return 0x06061556u; }
        if (index == 18) { return 0x4D732731u; }
        if (index == 19) { return 0x8DFFD880u; }
        if (index == 20) { return 0x14A06840u; }
        if (index == 21) { return 0x6599855Fu; }
        if (index == 22) { return 0x5EE61B08u; }
        if (index == 23) { return 0xA9E39161u; }
        if (index == 24) { return 0x9AF4361Du; }
        if (index == 25) { return 0xF0CFBC20u; }
        if (index == 26) { return 0xFC7B6BABu; }
        if (index == 27) { return 0x56033046u; }
        if (index == 28) { return 0x1F8D5D08u; }
        if (index == 29) { return 0x6BFB5FB1u; }
        if (index == 30) { return 0x8A5292EAu; }
        return 0x3D0739F7u;
    }

    private static uint TwoOverPi0_32(uint index) => index < 16 ? TwoOverPi0_16(index) : TwoOverPi16_32(index);

    private static uint TwoOverPi32_48(uint index)
    {
        if (index == 32) { return 0xEBE5F17Bu; }
        if (index == 33) { return 0x7527BAC7u; }
        if (index == 34) { return 0x9E5FEA2Du; }
        if (index == 35) { return 0x4F463F66u; }
        if (index == 36) { return 0x27CB09B7u; }
        if (index == 37) { return 0x6D367ECFu; }
        if (index == 38) { return 0x5A0A6D1Fu; }
        if (index == 39) { return 0xEF2F118Bu; }
        if (index == 40) { return 0xDE05980Fu; }
        if (index == 41) { return 0x1FF897FFu; }
        if (index == 42) { return 0xBDF9283Bu; }
        if (index == 43) { return 0x9C845F8Bu; }
        if (index == 44) { return 0x835339F4u; }
        if (index == 45) { return 0x3991D639u; }
        if (index == 46) { return 0xB45F7E41u; }
        return 0xE99C7026u;
    }

    private static uint TwoOverPi48_64(uint index)
    {
        if (index == 48) { return 0x2EBB4484u; }
        if (index == 49) { return 0xE88235F5u; }
        if (index == 50) { return 0xB129A73Eu; }
        if (index == 51) { return 0xFE1DEB1Cu; }
        if (index == 52) { return 0x09D1921Cu; }
        if (index == 53) { return 0x06492EEAu; }
        if (index == 54) { return 0x424DD2E0u; }
        if (index == 55) { return 0xB7246E3Au; }
        if (index == 56) { return 0xDEBBC561u; }
        if (index == 57) { return 0xFE5163ABu; }
        if (index == 58) { return 0x3C439041u; }
        if (index == 59) { return 0xDB629599u; }
        if (index == 60) { return 0xF534DDC0u; }
        if (index == 61) { return 0xFC2757D1u; }
        if (index == 62) { return 0x4E441529u; }
        return 0xA2F9836Eu;
    }

    private static uint TwoOverPi32_64(uint index) => index < 48 ? TwoOverPi32_48(index) : TwoOverPi48_64(index);

    private static uint TwoOverPi0_64(uint index) => index < 32 ? TwoOverPi0_32(index) : TwoOverPi32_64(index);
}
