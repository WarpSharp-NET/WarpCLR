using System.Reflection;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableCollectiveTests
{
    [TestMethod]
    public void EveryRuntimePrimitiveClosesOnWordsAndEveryBackendEmitsTheSameServices()
    {
        MethodInfo[] services = typeof(WarpPortableCollectiveServices).GetMethods(BindingFlags.Public | BindingFlags.Static);
        Assert.HasCount(11, services);
        foreach (ref readonly MethodInfo method in services.AsSpan())
        {
            Assert.AreEqual(typeof(uint), method.ReturnType);
            foreach (ref readonly ParameterInfo parameter in method.GetParameters().AsSpan())
            {
                Assert.IsTrue(parameter.ParameterType == typeof(uint) || parameter.ParameterType == typeof(uint[]));
            }
            WarpLogicalMachineLayout layout = WarpPortableCollectiveKernels.Lower(method);
            StringAssert.Contains(layout.Kernel.Name, WarpPortableCollectiveLayout.Semantics, StringComparison.Ordinal);
            StringAssert.Contains(layout.Kernel.Name, WarpPortableCollectiveArithmetic.Semantics, StringComparison.Ordinal);
            foreach (WarpBackendKind backend in new[] { WarpBackendKind.NVPTX, WarpBackendKind.AMDGPU, WarpBackendKind.SPIRV })
            {
                string source = WarpPortableMachineEmitter.Emit(layout, backend);
                StringAssert.Contains(source, "warp_resume", StringComparison.Ordinal);
                Assert.IsFalse(source.Contains(" fast ", StringComparison.Ordinal));
                Assert.IsFalse(source.Contains("fadd ", StringComparison.Ordinal));
                Assert.IsFalse(source.Contains("fmul ", StringComparison.Ordinal));
                Assert.IsFalse(source.Contains("uitofp ", StringComparison.Ordinal));
                Assert.IsFalse(source.Contains("llvm.sqrt", StringComparison.Ordinal));
            }
        }
    }
}
