namespace WarpCLR.Runtime.Device;

public readonly struct WarpManagedReference : IEquatable<WarpManagedReference>
{
    private readonly long activation;
    private readonly uint handle;

    internal WarpManagedReference(long activation, uint handle)
    {
        this.activation = activation;
        this.handle = handle;
    }

    public static WarpManagedReference Null => default;

    public bool IsNull => handle == 0;

    internal long Activation => activation;

    internal uint Handle => handle;

    public bool Equals(WarpManagedReference other) =>
        activation == other.activation && handle == other.handle;

    public override bool Equals(object? obj) =>
        obj is WarpManagedReference other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(activation, handle);

    public override string ToString() => IsNull ? "null" : "scoped-reference";

    public static bool operator ==(WarpManagedReference left, WarpManagedReference right) =>
        left.Equals(right);

    public static bool operator !=(WarpManagedReference left, WarpManagedReference right) =>
        !left.Equals(right);
}
