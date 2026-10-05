namespace WarpCLR.Runtime.Host;

internal static class WarpCoreCLRRecoveryCatalog
{
    internal const string Version = "warp.coreclr.stopped-remote-cleanup/typed-scheduler-v2-fixed-ir-controller-transition-raw-utf16-ir/0.3";
    internal const string CompareExchange = "2540ABA76A4F5ED896833806B0EDAE9DFFAA2D56FC7D6DC1B3ED5A77C6328960";
    internal const string DisposeStoppedCensus = "730C0875F249485EA5B35D5B9C0829850E35FAC61DC2D49147A433942B9D5E90";
    internal const string RequestCollection = "94703A34D5089B1CE86CC0AAF59FF0C8B36C652CE7BD9941C869BC0CAC99AE63";
    internal const string BeginDispatch = "2DE9E902E2C42D7D16EA213B96DD5AC15E9C37FE511A6D4DB8200C66149C8520";
    internal const string DisposeStoppedController = "D68A59E71BC1DC78B1F84FF12EFE1BD57F9B8B42625F0561A2189E0F75F7797A";

    internal static void Require(WarpCoreCLRCleanupPurpose purpose, string hash)
    {
        bool admitted = purpose switch
        {
            WarpCoreCLRCleanupPurpose.StoppedFault => hash is DisposeStoppedCensus or
                "3011E6E3C59E8D51DAA7BBF7C0D220AC8CD1681DADC2651AFA2494FBD796CBD4",
            WarpCoreCLRCleanupPurpose.LeaseDrain => hash is "3BC9946F84FD69DC38B7C06F4CB8896C50DC561DDB6C2768C9107EB5CF91A9E6",
            WarpCoreCLRCleanupPurpose.AcquireFreeController or WarpCoreCLRCleanupPurpose.ReleaseCapturedController or WarpCoreCLRCleanupPurpose.PublishControllerRelease => hash is CompareExchange,
            WarpCoreCLRCleanupPurpose.StoppedController => hash is DisposeStoppedController,
            WarpCoreCLRCleanupPurpose.CompleteCancellation => hash is
                "478F2D3214DCA65E4CDA6EDA439E4605E6B7EA821596F9EEDA32D87C41D14A4A" or
                "B3CC39353C6B98798504E124418ECDCF3A535A889FE451E795CCF06297DC51ED",
            WarpCoreCLRCleanupPurpose.DisposeContext => hash is
                "E1D311D42DEA34BFFD55AF093B8DAD667BAD058208CD21D5EFB8CCA29BE0B120" or
                "C80A0CB2E4875BA37F2F3FAC092BA47704F0F085E4FD137E5562943D92B5A848",
            _ => false,
        };
        if (!admitted) { throw new InvalidOperationException("Stopped recovery cannot admit an arbitrary kernel or changed trusted cleanup algorithm."); }
    }
}
