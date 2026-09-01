using System.Reflection;
using WarpCLR.IR;

namespace WarpCLR.Tests.Features;

[TestClass]
public sealed class IntegerMapFeatureTests
{
    [TestMethod]
    [FourBackends]
    public void Grayscale_map_has_exact_results(WarpBackendKind backend)
    {
        MethodInfo method = GetKernel(nameof(TestKernels.Grayscale));
        uint[] input =
        [
            0xFF000000u,
            0xFF0000FFu,
            0xFF00FF00u,
            0xFFFF0000u,
            0xFFFFFFFFu,
            0x80402010u,
        ];
        uint[] expected = input.Select(TestKernels.Grayscale).ToArray();

        uint[] actual = KernelTestHarness.CompileAndEmulate(
            method,
            1,
            backend,
            [input]);

        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    [FourBackends]
    public void Multiple_inputs_and_scalars_have_exact_results(WarpBackendKind backend)
    {
        MethodInfo method = GetKernel(nameof(TestKernels.Combine));
        uint[] left = [0u, 1u, uint.MaxValue, 0x80000000u, 0x12345678u];
        uint[] right = [9u, uint.MaxValue, 2u, 0x7FFFFFFFu, 0xABCDEF01u];
        uint mask = 0xA5A5A5A5u;
        uint shift = 37u;
        uint[] expected = left
            .Zip(right, (first, second) => TestKernels.Combine(first, second, mask, shift))
            .ToArray();

        uint[] actual = KernelTestHarness.CompileAndEmulate(
            method,
            2,
            backend,
            [left, right],
            [mask, shift]);

        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    [FourBackends]
    public void Wrapping_and_logical_shift_semantics_are_exact(WarpBackendKind backend)
    {
        MethodInfo method = GetKernel(nameof(TestKernels.Scramble));
        uint[] input = [0u, 1u, uint.MaxValue, 0x80000000u, 0xDEADBEEFu];
        uint shift = 63u;
        uint[] expected = input.Select(value => TestKernels.Scramble(value, shift)).ToArray();

        uint[] actual = KernelTestHarness.CompileAndEmulate(
            method,
            1,
            backend,
            [input],
            [shift]);

        CollectionAssert.AreEqual(expected, actual);
    }

    private static MethodInfo GetKernel(string name) =>
        typeof(TestKernels).GetMethod(name, BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Test kernel '{name}' was not found.");
}
