using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Text.Json;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests;

internal sealed class WarpBooleanProofFixture : IDisposable
{
    public static readonly string[] MethodNames =
        ["Map", "Uninitialized", "Branch", "Helper", "Merge", "Mixed", "Wide", "Loop", "Initialized", "UInt32Control"];

    private readonly AssemblyLoadContext context = new("boolean-storage-proof", isCollectible: true);
    private readonly string directory;
    private readonly Type entryType;

    public WarpBooleanProofFixture()
    {
        Bytes = CreateImage();
        directory = Directory.CreateTempSubdirectory("warpclr-boolean-proof-").FullName;
        try
        {
            string path = Path.Combine(directory, "BooleanLocalProof.dll");
            File.WriteAllBytes(path, Bytes);
            entryType = context.LoadFromAssemblyPath(path).GetType("WarpBooleanProof.Entry", throwOnError: true)!;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public byte[] Bytes { get; }

    public static string Identity(string method) => $"WarpBooleanProof.Entry.{method}";

    public MethodInfo Method(string name) => entryType.GetMethod(name)!;

    public void Dispose()
    {
        context.Unload();
        Directory.Delete(directory, recursive: true);
    }

    private static byte[] CreateImage()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("WarpBooleanLocalProof"), typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule("BooleanLocalProof");
        const TypeAttributes attributes = TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed;
        TypeBuilder entry = module.DefineType("WarpBooleanProof.Entry", attributes);
        TypeBuilder helperType = module.DefineType("WarpBooleanProof.Helpers", attributes);
        MethodBuilder helper = DefineMethod(helperType, "Truncate");
        EmitStoreAndLoad(helper, wide: false);
        foreach (string name in MethodNames)
        {
            MethodBuilder method = DefineMethod(entry, name, hasScalar: string.Equals(name, "Loop", StringComparison.Ordinal));
            EmitMethod(method, name, helper);
        }

        entry.CreateType();
        helperType.CreateType();
        string manifest = CreateManifest();
        ConstructorInfo metadata = typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!;
        assembly.SetCustomAttribute(new CustomAttributeBuilder(metadata, ["WarpCIL.Manifest", manifest]));
        using var stream = new MemoryStream();
        assembly.Save(stream);
        byte[] bytes = stream.ToArray();
        IReadOnlyDictionary<string, string> hashes = new WarpModuleVerifier().ComputeGraphHashes(bytes);
        for (int index = 0; index < MethodNames.Length; index++)
        {
            bytes = ManifestAssemblyFixture.ReplaceUtf8(bytes, Placeholder(index), hashes[Identity(MethodNames[index])]);
        }

        return bytes;
    }

    private static MethodBuilder DefineMethod(TypeBuilder type, string name, bool hasScalar = false) =>
        type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, typeof(uint),
            hasScalar ? [typeof(uint), typeof(uint)] : [typeof(uint)]);

    private static void EmitMethod(MethodBuilder method, string name, MethodBuilder helper)
    {
        if (name is "Map" or "Uninitialized" or "Wide" or "UInt32Control")
        {
            method.InitLocals = !string.Equals(name, "Uninitialized", StringComparison.Ordinal);
            EmitStoreAndLoad(method, string.Equals(name, "Wide", StringComparison.Ordinal),
                string.Equals(name, "UInt32Control", StringComparison.Ordinal));
            return;
        }

        ILGenerator il = method.GetILGenerator();
        if (string.Equals(name, "Helper", StringComparison.Ordinal))
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, helper);
            il.Emit(OpCodes.Ret);
            return;
        }

        if (string.Equals(name, "Mixed", StringComparison.Ordinal))
        {
            EmitMixed(il);
            return;
        }

        il.DeclareLocal(typeof(bool));
        if (string.Equals(name, "Initialized", StringComparison.Ordinal))
        {
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ret);
            return;
        }

        if (string.Equals(name, "Merge", StringComparison.Ordinal))
        {
            EmitMerge(il);
            return;
        }

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc_0);
        if (string.Equals(name, "Branch", StringComparison.Ordinal))
        {
            EmitBranch(il);
        }
        else
        {
            EmitLoop(il);
        }
    }

    private static void EmitLoop(ILGenerator il)
    {
        il.DeclareLocal(typeof(uint));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc_1);
        Label loop = il.DefineLabel();
        Label exit = il.DefineLabel();
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc_1);
        il.Emit(OpCodes.Brfalse_S, exit);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ldc_I4, 257);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc_0);
        il.Emit(OpCodes.Ldloc_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc_1);
        il.Emit(OpCodes.Br_S, loop);
        il.MarkLabel(exit);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitBranch(ILGenerator il)
    {
        Label zero = il.DefineLabel();
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Brfalse_S, zero);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(zero);
        il.Emit(OpCodes.Ldc_I4, 0xCAFE);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitMixed(ILGenerator il)
    {
        il.DeclareLocal(typeof(uint));
        il.DeclareLocal(typeof(bool));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc_S, (byte)1);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ldloc_S, (byte)1);
        il.Emit(OpCodes.Xor);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitMerge(ILGenerator il)
    {
        Label alternate = il.DefineLabel();
        Label merge = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brfalse_S, alternate);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc_0);
        il.Emit(OpCodes.Br_S, merge);
        il.MarkLabel(alternate);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc_0);
        il.MarkLabel(merge);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStoreAndLoad(MethodBuilder method, bool wide, bool uintLocal = false)
    {
        ILGenerator il = method.GetILGenerator();
        if (wide)
        {
            for (int index = 0; index < 256; index++)
            {
                il.DeclareLocal(typeof(uint));
            }
        }

        il.DeclareLocal(uintLocal ? typeof(uint) : typeof(bool));
        il.Emit(OpCodes.Ldarg_0);
        if (wide)
        {
            il.Emit(OpCodes.Stloc, (short)256);
            il.Emit(OpCodes.Ldloc, (short)256);
        }
        else
        {
            il.Emit(OpCodes.Stloc_0);
            il.Emit(OpCodes.Ldloc_0);
        }

        il.Emit(OpCodes.Ret);
    }

    private static string Placeholder(int index) => new((char)('0' + index), 64);

    private static string CreateManifest()
    {
        string capabilities = JsonSerializer.Serialize(WarpProfileCatalog.RequiredCapabilities);
        string entries = string.Join(',', MethodNames.Select((name, index) =>
        {
            string roles = string.Equals(name, "Loop", StringComparison.Ordinal) ? "[\"input\",\"scalar\"]" : "[\"input\"]";
            return $$"""{"type":"WarpBooleanProof.Entry","method":"{{name}}","execution":"map","parameterRoles":{{roles}},"capabilities":{{capabilities}},"graphHash":"{{Placeholder(index)}}"}""";
        }));
        return $$"""{"contract":"warpcil/0.1","producer":"WarpBooleanLocalProof","producerVersion":"0.1.0","entries":[{{entries}}],"hostImports":[],"extensions":[]}""";
    }
}
