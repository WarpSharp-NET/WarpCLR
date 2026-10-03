using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal static class WarpInitializationAdmission
{
    public static void RequireMethod(MethodInfo method)
    {
        string type = method.DeclaringType?.FullName ?? "<global>";
        WarpCompilationAdmission.Require("<reflection-method>", WarpCompilationResourceKind.IdentityCharacters,
            type.Length + (long)method.Name.Length + 1, WarpCompilationAdmission.MaximumIdentityCharacters);
        string identity = $"{type}.{method.Name}";
        if (method.GetMethodImplementationFlags().HasFlag(MethodImplAttributes.Synchronized))
        {
            throw SynchronizationError(identity);
        }

        if (method.DeclaringType?.TypeInitializer is not null)
        {
            throw InitializationError(identity);
        }
    }

    public static void RequireMethod(MetadataReader metadata, MethodDefinition method, string identity)
    {
        if (method.ImplAttributes.HasFlag(MethodImplAttributes.Synchronized))
        {
            throw SynchronizationError(identity);
        }

        if (HasInitializer(metadata, metadata.GetTypeDefinition(method.GetDeclaringType())))
        {
            throw InitializationError(identity);
        }
    }

    public static void RequireModule(MetadataReader metadata)
    {
        foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
        {
            TypeDefinition type = metadata.GetTypeDefinition(handle);
            if (metadata.StringComparer.Equals(type.Name, "<Module>") &&
                metadata.StringComparer.Equals(type.Namespace, string.Empty) && HasInitializer(metadata, type))
            {
                throw InitializationError("<Module>");
            }
        }
    }

    public static void RequireModule(Module module)
    {
        Type? globalType = module.GetType("<Module>", throwOnError: false, ignoreCase: false);
        if (globalType?.TypeInitializer is not null || module.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(method => string.Equals(method.Name, ".cctor", StringComparison.Ordinal)))
        {
            throw InitializationError("<Module>");
        }

        if (module.Assembly.IsDynamic)
        {
            return;
        }

        string path = module.FullyQualifiedName;
        if (!File.Exists(path))
        {
            throw new WarpVerificationException("WRPCIL1015", "The loaded module's initialization metadata is unavailable for safe reflection admission.");
        }

        using FileStream stream = File.OpenRead(path);
        WarpCompilationAdmission.Require("<module>", WarpCompilationResourceKind.AssemblyBytes, stream.Length, WarpCompilationAdmission.MaximumAssemblyBytes);
        using var reader = new PEReader(stream, PEStreamOptions.LeaveOpen);
        MetadataReader metadata = reader.GetMetadataReader();
        if (metadata.GetGuid(metadata.GetModuleDefinition().Mvid) != module.ModuleVersionId)
        {
            throw new WarpVerificationException("WRPCIL1015", "The loaded module and its initialization metadata have different identities.");
        }

        RequireModule(metadata);
    }

    private static bool HasInitializer(MetadataReader metadata, TypeDefinition type)
    {
        foreach (MethodDefinitionHandle handle in type.GetMethods())
        {
            if (metadata.StringComparer.Equals(metadata.GetMethodDefinition(handle).Name, ".cctor"))
            {
                return true;
            }
        }

        return false;
    }

    private static WarpVerificationException InitializationError(string identity) =>
        new("WRPCIL1015", $"Method or module '{identity}' requires implicit initialization outside the portable CLR profile.");

    private static WarpVerificationException SynchronizationError(string identity) =>
        new("WRPCIL1016", $"Method '{identity}' requires an implicit monitor outside the portable CLR profile.");
}
