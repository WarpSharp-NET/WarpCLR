namespace WarpCLR.Runtime.Host.Native;

internal interface IWarpNativeDriver : IDisposable
{
    WarpNativeTarget Target { get; }
    IWarpNativeModule Load(WarpNativeImage image);
}
