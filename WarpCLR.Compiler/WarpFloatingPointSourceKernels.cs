namespace WarpCLR.Compiler;

internal static class WarpFloatingPointSourceKernels
{
    public static float Pipeline32(float left, float right, float scale, float bias) =>
        (left + right) * (scale - bias) / (left - right);

    public static double Pipeline64(double left, double right, double scale, double bias) =>
        (left + right) * (scale - bias) / (left - right);

    public static float Transport32(float value) => value;

    public static double Transport64(double value) => value;
}
