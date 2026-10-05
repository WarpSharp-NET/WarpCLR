using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal static partial class WarpPortableClosedFrameExceptionCases
{
    internal static void ASecondLegitimateFrameViewCannotReplaceTheCompiledRootElementType()
    {
        var driver = new WarpPortableClosedFrameExceptionDriver(nameof(Sources.ProjectionTypes), flag: 0xFEDCBA98);
        WarpPortableMethodGraphMethod target = driver.Program.Graph.Methods.First(method =>
            string.Equals(method.SourceMethod.Name, nameof(SetUnsigned), StringComparison.Ordinal));
        WarpPortableWordBody body = driver.Program.Lowered.Bodies.First(body => string.Equals(body.MethodIdentity, target.Identity, StringComparison.Ordinal));
        driver.ExecuteUntil(() => AtBodyBoundary(driver, body.Function));
        int frame = WarpLogicalMachineLayout.HeaderWords + checked((int)driver.State[WarpLogicalMachineLayout.DepthOffset] - 1) * driver.Program.Layout.FrameWords;
        int ownerWord = frame + driver.Program.Layout.PrivateOffset + body.Arguments[0].WordOffset;
        uint[] owner = driver.State.AsSpan(ownerWord, 6).ToArray();
        Assert.AreEqual(driver.Program.Schema.TypeId(WarpPortableMethodGraphIdentity.Type(typeof(uint))), owner[5]);
        uint intType = driver.Program.Schema.TypeId(WarpPortableMethodGraphIdentity.Type(typeof(int)));
        uint[] replacement = (uint[])owner.Clone(); replacement[3] += 4; replacement[5] = intType;
        Assert.AreEqual(0u, WarpPortableExceptionTestService.Run(typeof(WarpPortableFrameServices),
            nameof(WarpPortableFrameServices.ValidateOwner), driver.State, replacement));
        int ownedFrame = WarpLogicalMachineLayout.HeaderWords + checked((int)(owner[1] - 1)) * driver.Program.Layout.FrameWords;
        Assert.AreEqual(0u, driver.Heap(nameof(WarpPortableHeapServices.ValidateSourceFrameView),
            driver.State[ownedFrame + WarpLogicalMachineLayout.FrameFunctionOffset],
            driver.State[ownedFrame + WarpLogicalMachineLayout.FramePrivateWordsOffset], replacement[3], replacement[4], replacement[5]));
        replacement.CopyTo(driver.State, ownerWord);
        uint stack = driver.Arena[driver.ScheduledWorker + WarpPortableSchedulerLayout.LogicalStackBase];
        uint[] previouslyPublished = driver.Arena.AsSpan((int)stack, WarpPortableClosedFrameExceptionDriver.RootCapacity * 3).ToArray();
        uint[] originalPrivate = driver.State.AsSpan(ownedFrame + driver.Program.Layout.PrivateOffset,
            checked((int)driver.State[ownedFrame + WarpLogicalMachineLayout.FramePrivateWordsOffset])).ToArray();
        Assert.ThrowsExactly<AssertFailedException>(driver.PublishBoundary);
        CollectionAssert.AreEqual(previouslyPublished,
            driver.Arena.AsSpan((int)stack, WarpPortableClosedFrameExceptionDriver.RootCapacity * 3).ToArray());
        CollectionAssert.AreEqual(originalPrivate, driver.State.AsSpan(ownedFrame + driver.Program.Layout.PrivateOffset, originalPrivate.Length).ToArray());
    }

    private static bool AtBodyBoundary(WarpPortableClosedFrameExceptionDriver driver, int function)
    {
        if (driver.State[WarpLogicalMachineLayout.SourceBoundaryStateOffset] != WarpLogicalMachineLayout.BeforeSourceBoundary) { return false; }
        int frame = WarpLogicalMachineLayout.HeaderWords + checked((int)driver.State[WarpLogicalMachineLayout.DepthOffset] - 1) * driver.Program.Layout.FrameWords;
        return driver.State[frame + WarpLogicalMachineLayout.FrameFunctionOffset] == (uint)function;
    }
}
