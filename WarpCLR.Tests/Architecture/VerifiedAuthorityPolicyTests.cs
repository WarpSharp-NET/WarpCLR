using System.Reflection;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Sdk;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Architecture;

[TestClass]
public sealed class VerifiedAuthorityPolicyTests
{
    [TestMethod]
    public void Public_api_requires_verified_module_intake()
    {
        MethodInfo[] publicCompileMethods = typeof(WarpBuildPipeline)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name.StartsWith("Compile", StringComparison.Ordinal))
            .ToArray();

        Assert.IsNotEmpty(publicCompileMethods);
        Assert.IsTrue(publicCompileMethods.All(method => method.Name == nameof(WarpBuildPipeline.CompileModule)));
        Assert.HasCount(0, typeof(WarpIntegerMapKernel).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.HasCount(0, typeof(WarpVerifiedModule).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.HasCount(0, typeof(WarpVerifiedEntry).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.IsFalse(typeof(WarpCompiler).IsPublic);
    }
}
