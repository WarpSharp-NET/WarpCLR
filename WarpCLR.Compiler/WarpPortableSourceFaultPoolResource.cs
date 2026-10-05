namespace WarpCLR.Compiler;

// Parameter-name literals retain their intern identity separately from resource data.
internal sealed record WarpPortableSourceFaultPoolResource(uint Id, string Identity, string Text, bool InternedLiteral);
