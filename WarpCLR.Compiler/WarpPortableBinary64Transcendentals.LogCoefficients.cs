namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64Transcendentals
{

    private static uint LogTailLow0_10(uint index)
    {
        if (index == 0) { return 0x00000000u; }
        if (index == 1) { return 0x55555555u; }
        if (index == 2) { return 0x9999999Au; }
        if (index == 3) { return 0x92492492u; }
        if (index == 4) { return 0x1C71C71Cu; }
        if (index == 5) { return 0x745D1746u; }
        if (index == 6) { return 0x13B13B14u; }
        if (index == 7) { return 0x11111111u; }
        if (index == 8) { return 0x1E1E1E1Eu; }
        return 0xBCA1AF28u;
    }

    private static uint LogTailLow10_21(uint index)
    {
        if (index == 10) { return 0x18618618u; }
        if (index == 11) { return 0x590B2164u; }
        if (index == 12) { return 0xEB851EB8u; }
        if (index == 13) { return 0xBDA12F68u; }
        if (index == 14) { return 0x611A7B96u; }
        if (index == 15) { return 0x08421084u; }
        if (index == 16) { return 0xF07C1F08u; }
        if (index == 17) { return 0x50750750u; }
        if (index == 18) { return 0x14C1BAD0u; }
        if (index == 19) { return 0x90690690u; }
        return 0xF3831F38u;
    }

    private static uint LogTailLow0_21(uint index) => index < 10 ? LogTailLow0_10(index) : LogTailLow10_21(index);

    private static uint LogTailLow(uint index) => LogTailLow0_21(index);

    private static uint LogTailHigh0_10(uint index)
    {
        if (index == 0) { return 0x00000000u; }
        if (index == 1) { return 0x3C755555u; }
        if (index == 2) { return 0xBC699999u; }
        if (index == 3) { return 0x3C624924u; }
        if (index == 4) { return 0x3C5C71C7u; }
        if (index == 5) { return 0xBC4745D1u; }
        if (index == 6) { return 0xBC53B13Bu; }
        if (index == 7) { return 0x3C311111u; }
        if (index == 8) { return 0x3C2E1E1Eu; }
        return 0x3C4AF286u;
    }

    private static uint LogTailHigh10_21(uint index)
    {
        if (index == 10) { return 0x3C486186u; }
        if (index == 11) { return 0x3C3642C8u; }
        if (index == 12) { return 0xBC2EB851u; }
        if (index == 13) { return 0x3C42F684u; }
        if (index == 14) { return 0x3C21A7B9u; }
        if (index == 15) { return 0x3C308421u; }
        if (index == 16) { return 0xBC2F07C1u; }
        if (index == 17) { return 0x3C307507u; }
        if (index == 18) { return 0xBC3BACF9u; }
        if (index == 19) { return 0x3C306906u; }
        return 0xBC2F3831u;
    }

    private static uint LogTailHigh0_21(uint index) => index < 10 ? LogTailHigh0_10(index) : LogTailHigh10_21(index);

    private static uint LogTailHigh(uint index) => LogTailHigh0_21(index);

}
