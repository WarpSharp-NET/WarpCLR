namespace WarpCLR.Runtime.Host;

internal sealed class WarpCompiledControllerGrant
{
    internal WarpCompiledControllerGrant(WarpCompiledController controller, uint token)
    {
        Controller = controller;
        Token = token;
    }

    internal WarpCompiledController Controller { get; }
    internal uint Token { get; }
    internal bool Released { get; set; }
}
