using System.Reflection;
using System.Runtime.CompilerServices;
using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

// The public entry is required by collectible emitted IL. It records only within
// a synchronous authenticated ordinary Worker invocation; it never grants Source.
public static partial class CoreCLRCallUseObservation
{
    internal const string Semantics = "warp.coreclr/actual-emission-private-call-site-authenticated-ordinary-command-before-callee-body/0.1";
    private static readonly ConditionalWeakTable<MethodInfo, Emission> Emissions = new();
    private static readonly object ScopeIssuer = new();
    private static readonly object EmissionIssuer = new();
    [ThreadStatic]
    private static Scope? current;

    public static void Record(object token, uint[][] inputs, uint[] scalars, int worker, uint[] state, uint[] arena, int frame)
    {
        Scope? scope = current;
        if (scope is null) { return; }
        // Validate the private compiler token before any candidate bank access.
        if (token is not Site site || !ReferenceEquals(site.Owner, scope.Emission) ||
            !site.Owner.IsActualSite(site))
        { throw new InvalidOperationException("The observed call site belongs to another actual emitted compilation."); }
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(scalars);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(arena);
        scope.Record(site, inputs, scalars, worker, state, arena, frame);
    }

    internal static void RegisterEmission(WarpLogicalMachineLayout layout, MethodInfo method, FieldInfo field)
    {
        if (!ReferenceEquals(field.DeclaringType, method.DeclaringType) || field.FieldType != typeof(object[]) ||
            !field.IsPrivate || !field.IsStatic || field.GetValue(null) is not null)
        { throw new InvalidOperationException("Call observations require a fresh private field in the exact emitted type."); }
        var emission = new Emission(EmissionIssuer, layout, method);
        field.SetValue(null, emission.Tokens);
        Emissions.Add(method, emission);
    }

    internal static void RequireEmission(WarpLogicalMachineLayout layout, MethodInfo method) => _ = FindEmission(layout, method);

    internal static Scope Begin(WarpCoreCLRWorkerProtocol.Frame frame, byte[] sessionKey, ulong sequence,
        CoreCLRResumableKernel kernel)
    {
        // ReadAsync's private table and the emission table precede decoding banks.
        byte[] payload = WarpCoreCLRWorkerProtocol.RequireAuthenticatedFrame(frame, sessionKey,
            WarpCoreCLRWorkerCallObservations.ExecuteObserved, sequence);
        Emission emission = FindEmission(kernel.Layout, kernel.CompiledEntryPoint);
        if (current is not null) { throw new InvalidOperationException("An authenticated observation cannot nest or cross invocations."); }
        var scope = new Scope(ScopeIssuer, emission, frame, sessionKey, sequence, payload);
        current = scope;
        return scope;
    }

    private static Emission FindEmission(WarpLogicalMachineLayout layout, MethodInfo method)
    {
        if (!Emissions.TryGetValue(method, out Emission? emission) || !ReferenceEquals(emission.Layout, layout))
        { throw new InvalidOperationException("An observation requires the exact compiler-issued layout and emitted method."); }
        return emission;
    }

    internal sealed class Emission
    {
        internal Emission(object issuer, WarpLogicalMachineLayout layout, MethodInfo method)
        {
            if (!ReferenceEquals(issuer, EmissionIssuer))
            { throw new InvalidOperationException("Only the actual emission issuer may create call-site provenance."); }
            Layout = layout; Method = method;
            Tokens = new object[layout.Nodes.Count];
            foreach (WarpLogicalMachineNode node in layout.Nodes)
            {
                if (node.Call is not null) { Tokens[node.ProgramCounter] = new Site(issuer, this, node); }
            }
        }
        internal WarpLogicalMachineLayout Layout { get; }
        internal MethodInfo Method { get; }
        internal object[] Tokens { get; }
        internal bool IsActualSite(Site site) => (uint)site.Node.ProgramCounter < (uint)Tokens.Length &&
            ReferenceEquals(Tokens[site.Node.ProgramCounter], site) &&
            ReferenceEquals(Layout.Nodes[site.Node.ProgramCounter], site.Node);
    }

    internal sealed class Site
    {
        internal Site(object issuer, Emission owner, WarpLogicalMachineNode node)
        {
            if (!ReferenceEquals(issuer, EmissionIssuer))
            { throw new InvalidOperationException("Only the actual emission issuer may create a call-site token."); }
            Owner = owner; Node = node;
        }
        internal Emission Owner { get; }
        internal WarpLogicalMachineNode Node { get; }
    }
}
