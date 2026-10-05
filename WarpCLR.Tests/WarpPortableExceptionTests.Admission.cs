using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableExceptionTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void CoreClrAndTypedVerifierRejectEhLexicallyNestedInsideTheSameMethodFilter()
    {
        MethodInfo entry = NestedFilter();
        MethodBody body = entry.GetMethodBody()!;
        Assert.HasCount(2, body.ExceptionHandlingClauses);
        ExceptionHandlingClause filter = body.ExceptionHandlingClauses.First(clause => clause.Flags == ExceptionHandlingClauseOptions.Filter);
        ExceptionHandlingClause nested = body.ExceptionHandlingClauses.First(clause => clause.Flags == ExceptionHandlingClauseOptions.Clause);
        Assert.IsTrue(nested.TryOffset >= filter.FilterOffset && nested.TryOffset < filter.HandlerOffset);
        // This direct CLR invocation is only an invalid-CIL reference oracle;
        // portable source execution never falls back to this method/delegate.
        InvalidProgramException rejected = Assert.ThrowsExactly<InvalidProgramException>(() =>
            entry.CreateDelegate<Func<Exception, int>>()(new InvalidOperationException("invalid-filter-reference")));
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(entry);
        WarpVerificationException denied = Assert.ThrowsExactly<WarpVerificationException>(() => WarpPortableTypedProgram.Verify(graph));
        Assert.AreEqual("WRPCLR2200", denied.Code, StringComparer.Ordinal);
        Assert.IsTrue(denied.Message.Contains("lexically enclosed by a filter", StringComparison.Ordinal));
        TestContext.WriteLine($"CoreCLR reference {rejected.GetType().FullName}; CIL {Convert.ToHexString(body.GetILAsByteArray()!)}; filter {filter.FilterOffset}..{filter.HandlerOffset}; nested try {nested.TryOffset}+{nested.TryLength}; compiler {denied.Code}@{denied.IlOffset}");
    }

    [TestMethod]
    public void ActualLoweredSourceUsesTheInvocationWorkerRatherThanAnyArenaOwnerInference()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            foreach (string method in new[] { nameof(WarpPortableExceptionFixtureSources.Catch), nameof(WarpPortableExceptionFixtureSources.SearchBeforeUnwind) })
            {
                bool filter = string.Equals(method, nameof(WarpPortableExceptionFixtureSources.SearchBeforeUnwind), StringComparison.Ordinal);
                var driver = new WarpPortableExceptionSourceDriver(method, quantum, flag: filter ? 1u : null,
                    logicalWorker: 2, workerCount: 3);
                driver.Execute();
                Assert.AreEqual(WarpLogicalMachineLayout.Completed, driver.State[WarpLogicalMachineLayout.StatusOffset], driver.Diagnostic());
                Assert.AreEqual(filter ? 31u : 17u, driver.State[WarpLogicalMachineLayout.ResultOffset]);
                Assert.IsTrue(driver.Program.Lowered.Kernel.Execution!.LogicalWorkerAccess);
                uint table = driver.Descriptor + driver.Arena[driver.Descriptor + WarpPortableExceptionLayout.WorkerStart];
                for (uint unused = 0; unused < 2; unused++)
                {
                    Assert.AreEqual(0u, driver.Arena[table + unused * WarpPortableExceptionLayout.WorkerWords + WarpPortableExceptionLayout.NextRaise]);
                }
                Assert.IsGreaterThanOrEqualTo(1u, driver.Arena[driver.Worker + WarpPortableExceptionLayout.NextRaise]);
                Assert.AreEqual(1u, driver.Arena[driver.Descriptor + WarpPortableExceptionLayout.NextTrace]);
                Assert.IsGreaterThan(3u, driver.Boundaries);
            }
        }
    }

    [TestMethod]
    public void ActualLoweredFilterMayCallAnotherSourceMethodThatHasItsOwnFilter()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            foreach (uint accept in new[] { 0u, 1u })
            {
                var driver = new WarpPortableExceptionSourceDriver(nameof(WarpPortableExceptionFixtureSources.CalledFilter), quantum,
                    replacementType: typeof(StackOverflowException), flag: accept);
                driver.Execute();
                Assert.AreEqual(WarpLogicalMachineLayout.Completed, driver.State[WarpLogicalMachineLayout.StatusOffset], driver.Diagnostic());
                Assert.AreEqual(accept == 0 ? 101u : 97u, driver.State[WarpLogicalMachineLayout.ResultOffset]);
                Assert.AreEqual(2u, driver.Arena[driver.Descriptor + WarpPortableExceptionLayout.NextTrace]);
                Assert.AreEqual(2, driver.Program.Lowered.Bodies.Count(body => body.AliasOwnerFunction != -1));
            }
        }
    }

    private static MethodInfo NestedFilter()
    {
        TypeBuilder type = AssemblyBuilder.DefineDynamicAssembly(new("InvalidNestedFilter_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect)
            .DefineDynamicModule("fixture").DefineType("InvalidNestedFilter", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        MethodBuilder method = type.DefineMethod("Invoke", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(Exception)]);
        ILGenerator code = method.GetILGenerator(); LocalBuilder result = code.DeclareLocal(typeof(int));
        code.BeginExceptionBlock(); code.Emit(OpCodes.Ldarg_0); code.Emit(OpCodes.Throw);
        code.BeginExceptFilterBlock(); code.Emit(OpCodes.Pop);
        code.BeginExceptionBlock(); code.Emit(OpCodes.Nop);
        code.BeginCatchBlock(typeof(Exception)); code.Emit(OpCodes.Pop); code.EndExceptionBlock();
        code.Emit(OpCodes.Ldc_I4_1); code.BeginCatchBlock(null); code.Emit(OpCodes.Pop);
        code.Emit(OpCodes.Ldc_I4_7); code.Emit(OpCodes.Stloc, result); code.EndExceptionBlock();
        code.Emit(OpCodes.Ldloc, result); code.Emit(OpCodes.Ret);
        return type.CreateType()!.GetMethod("Invoke")!;
    }

    [TestMethod]
    public void ActualCapturedFaultClauseRunsOnlyOnExceptionalUnwindAndMatchesClr()
    {
        MethodInfo source = WarpPortableExceptionCilFixtures.Fault;
        Assert.IsTrue(source.GetMethodBody()!.ExceptionHandlingClauses.Any(clause => clause.Flags == ExceptionHandlingClauseOptions.Fault));
        // Independent actual CLR oracle. These values cannot supply execution
        // results to the portable kernel or recover a failed portable dispatch.
        Func<Exception, bool, int> reference = source.CreateDelegate<Func<Exception, bool, int>>();
        foreach (int quantum in new[] { 31, 4096 })
        {
            foreach (uint throws in new[] { 0u, 1u })
            {
                int referenceValue = reference(new InvalidOperationException("fault-clause-reference"), throws != 0);
                Assert.AreEqual(throws == 0 ? 113 : 107, referenceValue, "The independent CLR fault oracle must match its explicit case.");
                var driver = new WarpPortableExceptionSourceDriver(WarpPortableExceptionCilFixtures.FaultName, quantum, flag: throws);
                driver.Execute();
                Assert.AreEqual(WarpLogicalMachineLayout.Completed, driver.State[WarpLogicalMachineLayout.StatusOffset], driver.Diagnostic());
                Assert.AreEqual((uint)referenceValue, driver.State[WarpLogicalMachineLayout.ResultOffset]);
                Assert.AreEqual(throws, driver.Arena[driver.Descriptor + WarpPortableExceptionLayout.NextTrace]);
            }
        }
    }
}
