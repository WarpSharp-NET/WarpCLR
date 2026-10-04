namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64Transcendentals
{

    private static uint Coefficient0Low0_11(uint index)
    {
        if (index == 0) { return 0x00000000u; }
        if (index == 1) { return 0x00000000u; }
        if (index == 2) { return 0x00000000u; }
        if (index == 3) { return 0x55555555u; }
        if (index == 4) { return 0x55555555u; }
        if (index == 5) { return 0x11111111u; }
        if (index == 6) { return 0x16C16C17u; }
        if (index == 7) { return 0x1A01A01Au; }
        if (index == 8) { return 0x1A01A01Au; }
        if (index == 9) { return 0xA556C734u; }
        return 0xB7789F5Cu;
    }

    private static uint Coefficient0Low11_23(uint index)
    {
        if (index == 11) { return 0x67F544E4u; }
        if (index == 12) { return 0xEFF8D898u; }
        if (index == 13) { return 0x13A86D09u; }
        if (index == 14) { return 0xA8C07C9Du; }
        if (index == 15) { return 0xE733B81Fu; }
        if (index == 16) { return 0xE733B81Fu; }
        if (index == 17) { return 0x7030AD4Au; }
        if (index == 18) { return 0x63B97D97u; }
        if (index == 19) { return 0x46814157u; }
        if (index == 20) { return 0xA4020225u; }
        if (index == 21) { return 0xF6DCF572u; }
        return 0x6DB7F853u;
    }

    private static uint Coefficient0Low0_23(uint index) => index < 11 ? Coefficient0Low0_11(index) : Coefficient0Low11_23(index);

    private static uint Coefficient0Low(uint index) => Coefficient0Low0_23(index);

    private static uint Coefficient0High0_11(uint index)
    {
        if (index == 0) { return 0x3FF00000u; }
        if (index == 1) { return 0x3FF00000u; }
        if (index == 2) { return 0x3FE00000u; }
        if (index == 3) { return 0x3FC55555u; }
        if (index == 4) { return 0x3FA55555u; }
        if (index == 5) { return 0x3F811111u; }
        if (index == 6) { return 0x3F56C16Cu; }
        if (index == 7) { return 0x3F2A01A0u; }
        if (index == 8) { return 0x3EFA01A0u; }
        if (index == 9) { return 0x3EC71DE3u; }
        return 0x3E927E4Fu;
    }

    private static uint Coefficient0High11_23(uint index)
    {
        if (index == 11) { return 0x3E5AE645u; }
        if (index == 12) { return 0x3E21EED8u; }
        if (index == 13) { return 0x3DE61246u; }
        if (index == 14) { return 0x3DA93974u; }
        if (index == 15) { return 0x3D6AE7F3u; }
        if (index == 16) { return 0x3D2AE7F3u; }
        if (index == 17) { return 0x3CE952C7u; }
        if (index == 18) { return 0x3CA68278u; }
        if (index == 19) { return 0x3C62F49Bu; }
        if (index == 20) { return 0x3C1E542Bu; }
        if (index == 21) { return 0x3BD71B8Eu; }
        return 0x3B90CE39u;
    }

    private static uint Coefficient0High0_23(uint index) => index < 11 ? Coefficient0High0_11(index) : Coefficient0High11_23(index);

    private static uint Coefficient0High(uint index) => Coefficient0High0_23(index);

    private static uint Coefficient1Low0_13(uint index)
    {
        if (index == 0) { return 0x00000000u; }
        if (index == 1) { return 0x55555555u; }
        if (index == 2) { return 0x11111111u; }
        if (index == 3) { return 0x1A01A01Au; }
        if (index == 4) { return 0xA556C734u; }
        if (index == 5) { return 0x67F544E4u; }
        if (index == 6) { return 0x13A86D09u; }
        if (index == 7) { return 0xE733B81Fu; }
        if (index == 8) { return 0x7030AD4Au; }
        if (index == 9) { return 0x46814157u; }
        if (index == 10) { return 0xF6DCF572u; }
        if (index == 11) { return 0x1316381Au; }
        return 0xDD165FA9u;
    }

    private static uint Coefficient1Low(uint index) => Coefficient1Low0_13(index);

    private static uint Coefficient1High0_13(uint index)
    {
        if (index == 0) { return 0x3FF00000u; }
        if (index == 1) { return 0xBFC55555u; }
        if (index == 2) { return 0x3F811111u; }
        if (index == 3) { return 0xBF2A01A0u; }
        if (index == 4) { return 0x3EC71DE3u; }
        if (index == 5) { return 0xBE5AE645u; }
        if (index == 6) { return 0x3DE61246u; }
        if (index == 7) { return 0xBD6AE7F3u; }
        if (index == 8) { return 0x3CE952C7u; }
        if (index == 9) { return 0xBC62F49Bu; }
        if (index == 10) { return 0x3BD71B8Eu; }
        if (index == 11) { return 0xBB4761B4u; }
        return 0x3AB3F3CCu;
    }

    private static uint Coefficient1High(uint index) => Coefficient1High0_13(index);

    private static uint Coefficient2Low0_13(uint index)
    {
        if (index == 0) { return 0x00000000u; }
        if (index == 1) { return 0x00000000u; }
        if (index == 2) { return 0x55555555u; }
        if (index == 3) { return 0x16C16C17u; }
        if (index == 4) { return 0x1A01A01Au; }
        if (index == 5) { return 0xB7789F5Cu; }
        if (index == 6) { return 0xEFF8D898u; }
        if (index == 7) { return 0xA8C07C9Du; }
        if (index == 8) { return 0xE733B81Fu; }
        if (index == 9) { return 0x63B97D97u; }
        if (index == 10) { return 0xA4020225u; }
        if (index == 11) { return 0x6DB7F853u; }
        return 0x1972F578u;
    }

    private static uint Coefficient2Low(uint index) => Coefficient2Low0_13(index);

    private static uint Coefficient2High0_13(uint index)
    {
        if (index == 0) { return 0x3FF00000u; }
        if (index == 1) { return 0xBFE00000u; }
        if (index == 2) { return 0x3FA55555u; }
        if (index == 3) { return 0xBF56C16Cu; }
        if (index == 4) { return 0x3EFA01A0u; }
        if (index == 5) { return 0xBE927E4Fu; }
        if (index == 6) { return 0x3E21EED8u; }
        if (index == 7) { return 0xBDA93974u; }
        if (index == 8) { return 0x3D2AE7F3u; }
        if (index == 9) { return 0xBCA68278u; }
        if (index == 10) { return 0x3C1E542Bu; }
        if (index == 11) { return 0xBB90CE39u; }
        return 0x3AFF2CF0u;
    }

    private static uint Coefficient2High(uint index) => Coefficient2High0_13(index);

    private static uint Coefficient3Low0_10(uint index)
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

    private static uint Coefficient3Low10_20(uint index)
    {
        if (index == 10) { return 0x18618618u; }
        if (index == 11) { return 0x590B2164u; }
        if (index == 12) { return 0x47AE147Bu; }
        if (index == 13) { return 0xBDA12F68u; }
        if (index == 14) { return 0x611A7B96u; }
        if (index == 15) { return 0x08421084u; }
        if (index == 16) { return 0xF07C1F08u; }
        if (index == 17) { return 0x1D41D41Du; }
        if (index == 18) { return 0x14C1BAD0u; }
        return 0x1A41A41Au;
    }

    private static uint Coefficient3Low0_20(uint index) => index < 10 ? Coefficient3Low0_10(index) : Coefficient3Low10_20(index);

    private static uint Coefficient3Low20_30(uint index)
    {
        if (index == 20) { return 0x8F9C18FAu; }
        if (index == 21) { return 0x417D05F4u; }
        if (index == 22) { return 0x16C16C17u; }
        if (index == 23) { return 0x2B931057u; }
        if (index == 24) { return 0xA72F0539u; }
        if (index == 25) { return 0x14141414u; }
        if (index == 26) { return 0xFB2B78C1u; }
        if (index == 27) { return 0x29E4129Eu; }
        if (index == 28) { return 0x7DC11F70u; }
        return 0x5F75270Du;
    }

    private static uint Coefficient3Low30_41(uint index)
    {
        if (index == 30) { return 0x4FBCDA3Bu; }
        if (index == 31) { return 0x10410410u; }
        if (index == 32) { return 0x1F81F820u; }
        if (index == 33) { return 0xABF0B767u; }
        if (index == 34) { return 0x76B981DBu; }
        if (index == 35) { return 0x89039B0Bu; }
        if (index == 36) { return 0x0381C0E0u; }
        if (index == 37) { return 0xB4E81B4Fu; }
        if (index == 38) { return 0x606A63BEu; }
        if (index == 39) { return 0x951033D9u; }
        return 0xFCD6E9E0u;
    }

    private static uint Coefficient3Low20_41(uint index) => index < 30 ? Coefficient3Low20_30(index) : Coefficient3Low30_41(index);

    private static uint Coefficient3Low0_41(uint index) => index < 20 ? Coefficient3Low0_20(index) : Coefficient3Low20_41(index);

    private static uint Coefficient3Low(uint index) => Coefficient3Low0_41(index);

    private static uint Coefficient3High0_10(uint index)
    {
        if (index == 0) { return 0x3FF00000u; }
        if (index == 1) { return 0xBFD55555u; }
        if (index == 2) { return 0x3FC99999u; }
        if (index == 3) { return 0xBFC24924u; }
        if (index == 4) { return 0x3FBC71C7u; }
        if (index == 5) { return 0xBFB745D1u; }
        if (index == 6) { return 0x3FB3B13Bu; }
        if (index == 7) { return 0xBFB11111u; }
        if (index == 8) { return 0x3FAE1E1Eu; }
        return 0xBFAAF286u;
    }

    private static uint Coefficient3High10_20(uint index)
    {
        if (index == 10) { return 0x3FA86186u; }
        if (index == 11) { return 0xBFA642C8u; }
        if (index == 12) { return 0x3FA47AE1u; }
        if (index == 13) { return 0xBFA2F684u; }
        if (index == 14) { return 0x3FA1A7B9u; }
        if (index == 15) { return 0xBFA08421u; }
        if (index == 16) { return 0x3F9F07C1u; }
        if (index == 17) { return 0xBF9D41D4u; }
        if (index == 18) { return 0x3F9BACF9u; }
        return 0xBF9A41A4u;
    }

    private static uint Coefficient3High0_20(uint index) => index < 10 ? Coefficient3High0_10(index) : Coefficient3High10_20(index);

    private static uint Coefficient3High20_30(uint index)
    {
        if (index == 20) { return 0x3F98F9C1u; }
        if (index == 21) { return 0xBF97D05Fu; }
        if (index == 22) { return 0x3F96C16Cu; }
        if (index == 23) { return 0xBF95C988u; }
        if (index == 24) { return 0x3F94E5E0u; }
        if (index == 25) { return 0xBF941414u; }
        if (index == 26) { return 0x3F93521Cu; }
        if (index == 27) { return 0xBF929E41u; }
        if (index == 28) { return 0x3F91F704u; }
        return 0xBF915B1Eu;
    }

    private static uint Coefficient3High30_41(uint index)
    {
        if (index == 30) { return 0x3F90C971u; }
        if (index == 31) { return 0xBF904104u; }
        if (index == 32) { return 0x3F8F81F8u; }
        if (index == 33) { return 0xBF8E9131u; }
        if (index == 34) { return 0x3F8DAE60u; }
        if (index == 35) { return 0xBF8CD856u; }
        if (index == 36) { return 0x3F8C0E07u; }
        if (index == 37) { return 0xBF8B4E81u; }
        if (index == 38) { return 0x3F8A98EFu; }
        if (index == 39) { return 0xBF89EC8Eu; }
        return 0x3F8948B0u;
    }

    private static uint Coefficient3High20_41(uint index) => index < 30 ? Coefficient3High20_30(index) : Coefficient3High30_41(index);

    private static uint Coefficient3High0_41(uint index) => index < 20 ? Coefficient3High0_20(index) : Coefficient3High20_41(index);

    private static uint Coefficient3High(uint index) => Coefficient3High0_41(index);

    private static uint Coefficient4Low0_10(uint index)
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

    private static uint Coefficient4Low10_21(uint index)
    {
        if (index == 10) { return 0x18618618u; }
        if (index == 11) { return 0x590B2164u; }
        if (index == 12) { return 0x47AE147Bu; }
        if (index == 13) { return 0xBDA12F68u; }
        if (index == 14) { return 0x611A7B96u; }
        if (index == 15) { return 0x08421084u; }
        if (index == 16) { return 0xF07C1F08u; }
        if (index == 17) { return 0x1D41D41Du; }
        if (index == 18) { return 0x14C1BAD0u; }
        if (index == 19) { return 0x1A41A41Au; }
        return 0x8F9C18FAu;
    }

    private static uint Coefficient4Low0_21(uint index) => index < 10 ? Coefficient4Low0_10(index) : Coefficient4Low10_21(index);

    private static uint Coefficient4Low(uint index) => Coefficient4Low0_21(index);

    private static uint Coefficient4High0_10(uint index)
    {
        if (index == 0) { return 0x3FF00000u; }
        if (index == 1) { return 0x3FD55555u; }
        if (index == 2) { return 0x3FC99999u; }
        if (index == 3) { return 0x3FC24924u; }
        if (index == 4) { return 0x3FBC71C7u; }
        if (index == 5) { return 0x3FB745D1u; }
        if (index == 6) { return 0x3FB3B13Bu; }
        if (index == 7) { return 0x3FB11111u; }
        if (index == 8) { return 0x3FAE1E1Eu; }
        return 0x3FAAF286u;
    }

    private static uint Coefficient4High10_21(uint index)
    {
        if (index == 10) { return 0x3FA86186u; }
        if (index == 11) { return 0x3FA642C8u; }
        if (index == 12) { return 0x3FA47AE1u; }
        if (index == 13) { return 0x3FA2F684u; }
        if (index == 14) { return 0x3FA1A7B9u; }
        if (index == 15) { return 0x3FA08421u; }
        if (index == 16) { return 0x3F9F07C1u; }
        if (index == 17) { return 0x3F9D41D4u; }
        if (index == 18) { return 0x3F9BACF9u; }
        if (index == 19) { return 0x3F9A41A4u; }
        return 0x3F98F9C1u;
    }

    private static uint Coefficient4High0_21(uint index) => index < 10 ? Coefficient4High0_10(index) : Coefficient4High10_21(index);

    private static uint Coefficient4High(uint index) => Coefficient4High0_21(index);

    private static uint Coefficient5Low0_10(uint index)
    {
        if (index == 0) { return 0x00000000u; }
        if (index == 1) { return 0x00000000u; }
        if (index == 2) { return 0x55555555u; }
        if (index == 3) { return 0x00000000u; }
        if (index == 4) { return 0x9999999Au; }
        if (index == 5) { return 0x55555555u; }
        if (index == 6) { return 0x92492492u; }
        if (index == 7) { return 0x00000000u; }
        if (index == 8) { return 0x1C71C71Cu; }
        return 0x9999999Au;
    }

    private static uint Coefficient5Low10_20(uint index)
    {
        if (index == 10) { return 0x745D1746u; }
        if (index == 11) { return 0x55555555u; }
        if (index == 12) { return 0x13B13B14u; }
        if (index == 13) { return 0x92492492u; }
        if (index == 14) { return 0x11111111u; }
        if (index == 15) { return 0x00000000u; }
        if (index == 16) { return 0x1E1E1E1Eu; }
        if (index == 17) { return 0x1C71C71Cu; }
        if (index == 18) { return 0xBCA1AF28u; }
        return 0x9999999Au;
    }

    private static uint Coefficient5Low0_20(uint index) => index < 10 ? Coefficient5Low0_10(index) : Coefficient5Low10_20(index);

    private static uint Coefficient5Low20_30(uint index)
    {
        if (index == 20) { return 0x18618618u; }
        if (index == 21) { return 0x745D1746u; }
        if (index == 22) { return 0x590B2164u; }
        if (index == 23) { return 0x55555555u; }
        if (index == 24) { return 0x47AE147Bu; }
        if (index == 25) { return 0x13B13B14u; }
        if (index == 26) { return 0xBDA12F68u; }
        if (index == 27) { return 0x92492492u; }
        if (index == 28) { return 0x611A7B96u; }
        return 0x11111111u;
    }

    private static uint Coefficient5Low30_41(uint index)
    {
        if (index == 30) { return 0x08421084u; }
        if (index == 31) { return 0x00000000u; }
        if (index == 32) { return 0xF07C1F08u; }
        if (index == 33) { return 0x1E1E1E1Eu; }
        if (index == 34) { return 0x1D41D41Du; }
        if (index == 35) { return 0x1C71C71Cu; }
        if (index == 36) { return 0x14C1BAD0u; }
        if (index == 37) { return 0xBCA1AF28u; }
        if (index == 38) { return 0x1A41A41Au; }
        if (index == 39) { return 0x9999999Au; }
        return 0x8F9C18FAu;
    }

    private static uint Coefficient5Low20_41(uint index) => index < 30 ? Coefficient5Low20_30(index) : Coefficient5Low30_41(index);

    private static uint Coefficient5Low0_41(uint index) => index < 20 ? Coefficient5Low0_20(index) : Coefficient5Low20_41(index);

    private static uint Coefficient5Low(uint index) => Coefficient5Low0_41(index);

    private static uint Coefficient5High0_10(uint index)
    {
        if (index == 0) { return 0x3FF00000u; }
        if (index == 1) { return 0xBFE00000u; }
        if (index == 2) { return 0x3FD55555u; }
        if (index == 3) { return 0xBFD00000u; }
        if (index == 4) { return 0x3FC99999u; }
        if (index == 5) { return 0xBFC55555u; }
        if (index == 6) { return 0x3FC24924u; }
        if (index == 7) { return 0xBFC00000u; }
        if (index == 8) { return 0x3FBC71C7u; }
        return 0xBFB99999u;
    }

    private static uint Coefficient5High10_20(uint index)
    {
        if (index == 10) { return 0x3FB745D1u; }
        if (index == 11) { return 0xBFB55555u; }
        if (index == 12) { return 0x3FB3B13Bu; }
        if (index == 13) { return 0xBFB24924u; }
        if (index == 14) { return 0x3FB11111u; }
        if (index == 15) { return 0xBFB00000u; }
        if (index == 16) { return 0x3FAE1E1Eu; }
        if (index == 17) { return 0xBFAC71C7u; }
        if (index == 18) { return 0x3FAAF286u; }
        return 0xBFA99999u;
    }

    private static uint Coefficient5High0_20(uint index) => index < 10 ? Coefficient5High0_10(index) : Coefficient5High10_20(index);

    private static uint Coefficient5High20_30(uint index)
    {
        if (index == 20) { return 0x3FA86186u; }
        if (index == 21) { return 0xBFA745D1u; }
        if (index == 22) { return 0x3FA642C8u; }
        if (index == 23) { return 0xBFA55555u; }
        if (index == 24) { return 0x3FA47AE1u; }
        if (index == 25) { return 0xBFA3B13Bu; }
        if (index == 26) { return 0x3FA2F684u; }
        if (index == 27) { return 0xBFA24924u; }
        if (index == 28) { return 0x3FA1A7B9u; }
        return 0xBFA11111u;
    }

    private static uint Coefficient5High30_41(uint index)
    {
        if (index == 30) { return 0x3FA08421u; }
        if (index == 31) { return 0xBFA00000u; }
        if (index == 32) { return 0x3F9F07C1u; }
        if (index == 33) { return 0xBF9E1E1Eu; }
        if (index == 34) { return 0x3F9D41D4u; }
        if (index == 35) { return 0xBF9C71C7u; }
        if (index == 36) { return 0x3F9BACF9u; }
        if (index == 37) { return 0xBF9AF286u; }
        if (index == 38) { return 0x3F9A41A4u; }
        if (index == 39) { return 0xBF999999u; }
        return 0x3F98F9C1u;
    }

    private static uint Coefficient5High20_41(uint index) => index < 30 ? Coefficient5High20_30(index) : Coefficient5High30_41(index);

    private static uint Coefficient5High0_41(uint index) => index < 20 ? Coefficient5High0_20(index) : Coefficient5High20_41(index);

    private static uint Coefficient5High(uint index) => Coefficient5High0_41(index);

    private static uint CoefficientLow(uint kind, uint index)
    {
        if (kind == 0) { return Coefficient0Low(index); }
        if (kind == 1) { return Coefficient1Low(index); }
        if (kind == 2) { return Coefficient2Low(index); }
        if (kind == 3) { return Coefficient3Low(index); }
        if (kind == 4) { return Coefficient4Low(index); }
        return Coefficient5Low(index);
    }

    private static uint CoefficientHigh(uint kind, uint index)
    {
        if (kind == 0) { return Coefficient0High(index); }
        if (kind == 1) { return Coefficient1High(index); }
        if (kind == 2) { return Coefficient2High(index); }
        if (kind == 3) { return Coefficient3High(index); }
        if (kind == 4) { return Coefficient4High(index); }
        return Coefficient5High(index);
    }

}
