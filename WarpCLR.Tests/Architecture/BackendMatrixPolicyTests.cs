using System.Reflection;
using WarpCLR.IR;

namespace WarpCLR.Tests.Architecture;

[TestClass]
public sealed class BackendMatrixPolicyTests
{
    [TestMethod]
    public void Every_feature_and_regression_test_uses_the_four_backend_matrix()
    {
        MethodInfo[] testMethods = typeof(BackendMatrixPolicyTests).Assembly
            .GetTypes()
            .Where(IsFeatureOrRegressionType)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(method => method.GetCustomAttribute<TestMethodAttribute>() is not null)
            .ToArray();

        Assert.IsNotEmpty(testMethods);

        foreach (MethodInfo method in testMethods)
        {
            FourBackendsAttribute[] attributes = method
                .GetCustomAttributes<FourBackendsAttribute>()
                .ToArray();
            Assert.HasCount(1, attributes, $"{method.DeclaringType?.FullName}.{method.Name}");

            ParameterInfo[] parameters = method.GetParameters();
            Assert.HasCount(1, parameters, $"{method.DeclaringType?.FullName}.{method.Name}");
            Assert.AreEqual(typeof(WarpBackendKind), parameters[0].ParameterType);

            WarpBackendKind[] actual = attributes[0]
                .GetData(method)
                .Select(row => (WarpBackendKind)row[0]!)
                .ToArray();
            CollectionAssert.AreEqual(WarpBackendCatalog.Required.ToArray(), actual);
        }
    }

    private static bool IsFeatureOrRegressionType(Type type) =>
        type.Namespace is not null &&
        (type.Namespace.StartsWith("WarpCLR.Tests.Features", StringComparison.Ordinal) ||
         type.Namespace.StartsWith("WarpCLR.Tests.Regressions", StringComparison.Ordinal));
}
