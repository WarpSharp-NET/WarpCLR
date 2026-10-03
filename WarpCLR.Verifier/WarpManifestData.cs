using System.Buffers;
using System.Text;
using System.Text.Json;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal sealed record WarpManifestData(
    string Contract,
    string Producer,
    string ProducerVersion,
    IReadOnlyList<WarpManifestEntryData> Entries);
