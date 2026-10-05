using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace WarpCLR.Verifier;

internal static class WarpPortableTypeInitializerName
{
    internal const string Semantics = "warp.type-initializer-exception-name/coreclr10-own-typedef-namespace-simple-name-no-enclosing-or-generic-display-raw-utf16/0.1";

    internal static ImmutableArray<ushort> Capture(Type source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Type definition = source.IsConstructedGenericType ? source.GetGenericTypeDefinition() : source;
        string name; string? space;
        if (definition.Assembly.IsDynamic)
        {
            // Reflection Namespace inherits the enclosing namespace. That is not
            // the native TypeDef namespace for a nested type, so do not guess it.
            if (definition.IsNested) { throw Invalid("An in-memory nested TypeDef requires its exact own metadata namespace for initializer exception data."); }
            name = definition.Name; space = definition.Namespace;
        }
        else
        {
            using FileStream stream = File.OpenRead(definition.Module.FullyQualifiedName);
            using var image = new PEReader(stream);
            MetadataReader reader = image.GetMetadataReader();
            EntityHandle token = MetadataTokens.EntityHandle(definition.MetadataToken);
            if (token.Kind != HandleKind.TypeDefinition) { throw Invalid("An initializer exception name must come from its exact TypeDef metadata row."); }
            TypeDefinition metadata = reader.GetTypeDefinition((TypeDefinitionHandle)token);
            name = reader.GetString(metadata.Name); space = reader.GetString(metadata.Namespace);
        }
        string qualified = string.IsNullOrEmpty(space) ? name : space + "." + name;
        return qualified.Select(character => (ushort)character).ToImmutableArray();
    }

    private static WarpVerificationException Invalid(string message) => new("WRPCLR2460", message, 0);
}
