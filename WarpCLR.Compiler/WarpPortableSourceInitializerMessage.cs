using System.Collections.Immutable;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

// Only immutable standard Corelib resource data and captured metadata are read.
// This never runs the original initializer, an Exception constructor or a formatter.
internal static class WarpPortableSourceInitializerMessage
{
    internal const string Semantics = "warp.type-initialization-message/captured-corelib-ui-culture-finite-string-argument-zero-raw-utf16-template/0.1";
    internal const string ResourceKey = "TypeInitialization_Type";

    internal static ImmutableArray<ushort> Materialize(string template, ImmutableArray<ushort> typeName)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (typeName.IsDefault) { throw new ArgumentException("The captured type name must be initialized.", nameof(typeName)); }
        var result = ImmutableArray.CreateBuilder<ushort>();
        for (int index = 0; index < template.Length; index++)
        {
            char unit = template[index];
            if (unit is '{' or '}')
            {
                if (index + 1 < template.Length && template[index + 1] == unit) { result.Add(unit); index++; }
                else if (unit == '{' && index + 2 < template.Length && template[index + 1] == '0' && template[index + 2] == '}')
                {
                    result.AddRange(typeName); index += 2;
                }
                else { throw new WarpVerificationException("WRPCLR2490", "The captured initializer resource needs an unimplemented formatting shape.", index); }
            }
            else { result.Add(unit); }
            WarpCompilationAdmission.Require(ResourceKey, WarpCompilationResourceKind.VerifierWorkspaceSlots,
                result.Count, WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
        }
        return result.ToImmutable();
    }
}
