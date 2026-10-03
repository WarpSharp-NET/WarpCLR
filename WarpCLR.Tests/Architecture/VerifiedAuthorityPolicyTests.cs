using System.Reflection;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Sdk;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Architecture;

[TestClass]
[global::System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest creates this internal fixture through reflected discovery.")]
internal sealed class VerifiedAuthorityPolicyTests
{
    [TestMethod]
    public void PublicApiRequiresVerifiedModuleIntake()
    {
        MethodInfo[] publicCompileMethods = typeof(WarpBuildPipeline)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name.StartsWith("Compile", StringComparison.Ordinal))
            .ToArray();

        Assert.IsNotEmpty(publicCompileMethods);
        Assert.IsTrue(publicCompileMethods.All(method =>
            method.Name is nameof(WarpBuildPipeline.CompileModule) or nameof(WarpBuildPipeline.CompilePackage)));
        Assert.HasCount(0, typeof(WarpIntegerMapKernel).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.HasCount(0, typeof(WarpVerifiedModule).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.HasCount(0, typeof(WarpVerifiedEntry).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.IsFalse(typeof(WarpCompiler).IsPublic);
    }
}
