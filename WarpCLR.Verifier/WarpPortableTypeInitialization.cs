using System.Reflection;

namespace WarpCLR.Verifier;

// Metadata capture never calls a constructor or RuntimeHelpers.RunClassConstructor.
// BeforeFieldInit uses the deterministic portable choice of first static-field access.
internal static class WarpPortableTypeInitialization
{
    internal const string Semantics = "warp.type-initialization/captured-ecma335-dotnet10-triggers-beforefieldinit-first-static-field-no-base-propagation/0.1";

    internal static WarpPortableTypedInitializerTrigger? ForMethod(WarpPortableMethodGraph graph, MethodBase target)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(target);
        Type? declaring = target.DeclaringType;
        if (declaring is null || target is ConstructorInfo { IsStatic: true }) { return null; }
        WarpPortableMethodGraphType? type = graph.Types.FirstOrDefault(type => type.SourceType == declaring);
        if (type?.Initializer is null || BeforeFieldInit(type)) { return null; }
        WarpPortableInitializerTriggerKind? kind = target.IsStatic ? WarpPortableInitializerTriggerKind.StaticMethod :
            target is ConstructorInfo ? WarpPortableInitializerTriggerKind.InstanceConstructor :
            declaring.IsValueType ? WarpPortableInitializerTriggerKind.ValueInstanceMethod :
            declaring.IsInterface ? WarpPortableInitializerTriggerKind.InterfaceInstanceMethod : null;
        return kind is { } value ? new(type.Identity, type.Initializer, value, false) : null;
    }

    internal static WarpPortableTypedInitializerTrigger? ForField(WarpPortableMethodGraph graph, WarpPortableMethodGraphField field)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(field);
        if (!field.IsStatic || field.IsLiteral) { return null; }
        WarpPortableMethodGraphType type = graph.Types.First(type => string.Equals(type.Identity, field.DeclaringType, StringComparison.Ordinal));
        return type.Initializer is { } initializer ? new(type.Identity, initializer,
            WarpPortableInitializerTriggerKind.StaticField, BeforeFieldInit(type)) : null;
    }

    private static bool BeforeFieldInit(WarpPortableMethodGraphType type) =>
        type.SourceType.Attributes.HasFlag(TypeAttributes.BeforeFieldInit);
}
