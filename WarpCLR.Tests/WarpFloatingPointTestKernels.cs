namespace WarpCLR.Tests.Production;

internal static class WarpFloatingPointTestKernels
{
    public static double Narrow(double value) => (double)(float)value;

    public static double Constant(double value) => (value + 0.1) - value;

    public static float Round32(float left, float right, float bias) => left * right + bias;

    public static double Round64(double left, double right, double bias) => left * right + bias;

    public static float Local32(float left, float right)
    {
        float sum = left + right;
        left = sum;
        return sum * left;
    }

    public static double Local64(double left, double right)
    {
        double sum = left + right;
        left = sum;
        return sum * left;
    }
}
