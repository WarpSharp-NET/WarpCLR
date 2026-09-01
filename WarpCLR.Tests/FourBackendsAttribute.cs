using System.Reflection;
using WarpCLR.IR;

namespace WarpCLR.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class FourBackendsAttribute : Attribute, ITestDataSource
{
    public IEnumerable<object?[]> GetData(MethodInfo methodInfo)
    {
        ArgumentNullException.ThrowIfNull(methodInfo);

        return WarpBackendCatalog.Required.Select(
            backend => new object?[] { backend });
    }

    public string? GetDisplayName(MethodInfo methodInfo, object?[]? data)
    {
        ArgumentNullException.ThrowIfNull(methodInfo);
        ArgumentNullException.ThrowIfNull(data);

        return $"{methodInfo.Name} ({data[0]})";
    }
}
