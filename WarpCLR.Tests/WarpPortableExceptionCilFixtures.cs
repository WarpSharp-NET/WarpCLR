using System.Reflection;
using System.Reflection.Emit;

namespace WarpCLR.Tests.Production;

internal static class WarpPortableExceptionCilFixtures
{
    internal const string FaultName = "SyntheticFault";
    internal static MethodInfo Fault { get; } = BuildFault();

    private static MethodInfo BuildFault()
    {
        TypeBuilder type = AssemblyBuilder.DefineDynamicAssembly(new("ActualFaultClause_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect)
            .DefineDynamicModule("fixture").DefineType("ActualFaultClause", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        MethodBuilder method = type.DefineMethod(FaultName, MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(Exception), typeof(bool)]);
        ILGenerator code = method.GetILGenerator(); LocalBuilder result = code.DeclareLocal(typeof(int));
        Label normal = code.DefineLabel();
        code.BeginExceptionBlock();
        code.BeginExceptionBlock(); code.Emit(OpCodes.Ldarg_1); code.Emit(OpCodes.Brfalse, normal);
        code.Emit(OpCodes.Ldarg_0); code.Emit(OpCodes.Throw);
        code.MarkLabel(normal); code.Emit(OpCodes.Ldc_I4, 113); code.Emit(OpCodes.Stloc, result);
        code.BeginFaultBlock(); code.Emit(OpCodes.Ldc_I4, 107); code.Emit(OpCodes.Stloc, result); code.EndExceptionBlock();
        code.BeginCatchBlock(typeof(InvalidOperationException)); code.Emit(OpCodes.Pop); code.EndExceptionBlock();
        code.Emit(OpCodes.Ldloc, result); code.Emit(OpCodes.Ret);
        return type.CreateType()!.GetMethod(FaultName)!;
    }
}
