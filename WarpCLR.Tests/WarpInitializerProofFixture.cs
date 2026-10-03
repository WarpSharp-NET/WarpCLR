using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Text.Json;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests;

internal sealed class WarpInitializerProofFixture : IDisposable
{
    public const string EntryIdentity = "WarpProbe.Entry.Map";

    private readonly AssemblyLoadContext context = new("initializer-proof", isCollectible: true);
    private readonly string directory;

    public WarpInitializerProofFixture(string scenario)
    {
        Bytes = CreateImage(scenario);
        directory = Directory.CreateTempSubdirectory("warpclr-init-proof-").FullName;
        try
        {
            string path = Path.Combine(directory, "probe.dll");
            File.WriteAllBytes(path, Bytes);
            Assembly assembly = context.LoadFromAssemblyPath(path);
            Method = assembly.GetType("WarpProbe.Entry", throwOnError: true)!.GetMethod("Map")!;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public byte[] Bytes { get; }

    public MethodInfo Method { get; }

    public void Dispose()
    {
        context.Unload();
        Directory.Delete(directory, recursive: true);
    }

    private static byte[] CreateImage(string scenario)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("WarpInitializerProof"), typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule("probe");
        const TypeAttributes attributes = TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed;
        TypeBuilder entry = module.DefineType("WarpProbe.Entry", attributes);
        TypeBuilder helper = module.DefineType("WarpProbe.Helper", attributes);
        TypeBuilder host = module.DefineType("WarpProbe.Host", attributes);
        MethodBuilder helperMethod = DefineMap(helper, "Add", string.Equals(scenario, "synchronized-helper", StringComparison.Ordinal));
        EmitMap(helperMethod.GetILGenerator(), null);
        MethodBuilder entryMethod = DefineMap(entry, "Map", string.Equals(scenario, "synchronized-entry", StringComparison.Ordinal));
        EmitMap(entryMethod.GetILGenerator(), scenario is "helper-initializer" or "synchronized-helper" ? helperMethod : null);
        AppendInitializer(module, entry, helper, host, scenario);
        entry.CreateType();
        helper.CreateType();
        host.CreateType();
        module.CreateGlobalFunctions();
        string manifest = CreateManifest();
        ConstructorInfo metadataConstructor = typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!;
        assembly.SetCustomAttribute(new CustomAttributeBuilder(metadataConstructor, ["WarpCIL.Manifest", manifest]));
        using var stream = new MemoryStream();
        assembly.Save(stream);
        byte[] bytes = stream.ToArray();
        if (string.Equals(scenario, "unrelated-initializer", StringComparison.Ordinal))
        {
            string graph = new WarpModuleVerifier().ComputeGraphHashes(bytes)[EntryIdentity];
            bytes = ManifestAssemblyFixture.ReplaceUtf8(bytes, new string('0', 64), graph);
        }

        return bytes;
    }

    private static MethodBuilder DefineMap(TypeBuilder type, string name, bool synchronized)
    {
        MethodBuilder method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static,
            typeof(uint), [typeof(uint), typeof(uint)]);
        if (synchronized)
        {
            method.SetImplementationFlags(MethodImplAttributes.IL | MethodImplAttributes.Synchronized);
        }

        return method;
    }

    private static void EmitMap(ILGenerator il, MethodBuilder? helper)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        if (helper is null)
        {
            il.Emit(OpCodes.Add);
        }
        else
        {
            il.Emit(OpCodes.Call, helper);
        }

        il.Emit(OpCodes.Ret);
    }

    private static void AppendInitializer(ModuleBuilder module, TypeBuilder entry, TypeBuilder helper, TypeBuilder host, string scenario)
    {
        if (string.Equals(scenario, "module-initializer", StringComparison.Ordinal))
        {
            MethodBuilder constructor = module.DefineGlobalMethod(".cctor",
                MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                typeof(void), Type.EmptyTypes);
            constructor.GetILGenerator().Emit(OpCodes.Ret);
            return;
        }

        TypeBuilder? owner = scenario switch
        {
            "entry-initializer" => entry,
            "helper-initializer" => helper,
            "unrelated-initializer" => host,
            _ => null,
        };
        if (owner is not null)
        {
            ILGenerator il = owner.DefineTypeInitializer().GetILGenerator();
            il.Emit(OpCodes.Ldstr, "Initializer execution is forbidden in this metadata-only admission proof.");
            il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
            il.Emit(OpCodes.Throw);
        }
    }

    private static string CreateManifest()
    {
        string capabilities = JsonSerializer.Serialize(WarpProfileCatalog.RequiredCapabilities);
        return $$"""{"contract":"warpcil/0.1","producer":"WarpInitializerProof","producerVersion":"0.1.0","entries":[{"type":"WarpProbe.Entry","method":"Map","execution":"map","parameterRoles":["input","scalar"],"capabilities":{{capabilities}},"graphHash":"{{new string('0', 64)}}"}],"hostImports":[],"extensions":[]}""";
    }
}
