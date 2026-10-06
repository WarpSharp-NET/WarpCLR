using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;

namespace WarpCLR.Tests;

// The persisted source has a strict .cctor. The fixture never invokes it on
// the host; metadata/CIL discovery and generated child execution are separate.
internal sealed class WarpSourceEndInitializer : IDisposable
{
    private readonly AssemblyLoadContext context = new("source-end-cctor", isCollectible: true);
    private readonly string directory = Directory.CreateTempSubdirectory("warp-source-end-cctor-").FullName;

    internal WarpSourceEndInitializer()
    {
        string path = Path.Combine(directory, "source.dll");
        try
        {
            File.WriteAllBytes(path, CreateImage());
            Method = context.LoadFromAssemblyPath(path).GetType("WarpEndInitializer", throwOnError: true)!.GetMethod("Read")!;
        }
        catch { Dispose(); throw; }
    }

    internal MethodInfo Method { get; }

    public void Dispose() { context.Unload(); Directory.Delete(directory, recursive: true); }

    private static byte[] CreateImage()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("WarpSourceEndCctor"), typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule("source");
        TypeBuilder type = module.DefineType("WarpEndInitializer", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        FieldBuilder field = type.DefineField("Value", typeof(ulong), FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly);
        ILGenerator initializer = type.DefineTypeInitializer().GetILGenerator();
        initializer.Emit(OpCodes.Ldc_I8, unchecked((long)0x8123456789ABCDEF)); initializer.Emit(OpCodes.Stsfld, field); initializer.Emit(OpCodes.Ret);
        ILGenerator read = type.DefineMethod("Read", MethodAttributes.Public | MethodAttributes.Static, typeof(ulong), [typeof(ulong)]).GetILGenerator();
        read.Emit(OpCodes.Ldsfld, field); read.Emit(OpCodes.Ldarg_0); read.Emit(OpCodes.Xor); read.Emit(OpCodes.Ret);
        type.CreateType(); module.CreateGlobalFunctions();
        using var stream = new MemoryStream(); assembly.Save(stream); return stream.ToArray();
    }
}
