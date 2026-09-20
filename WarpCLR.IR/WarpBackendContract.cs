using System.Collections.ObjectModel;

namespace WarpCLR.IR;

public sealed class WarpBackendContract
{
    private readonly ReadOnlyCollection<WarpIrOpCode> instructions;
    private readonly ReadOnlyCollection<WarpReductionOperation> reductions;

    public WarpBackendContract(
        string profileId,
        IEnumerable<WarpIrOpCode> instructions,
        IEnumerable<WarpReductionOperation> reductions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(reductions);

        WarpIrOpCode[] instructionArray = instructions.ToArray();
        WarpReductionOperation[] reductionArray = reductions.ToArray();
        ValidateValues(instructionArray, nameof(instructions));
        ValidateValues(reductionArray, nameof(reductions));

        ProfileId = profileId;
        this.instructions = Array.AsReadOnly(instructionArray);
        this.reductions = Array.AsReadOnly(reductionArray);
    }

    public string ProfileId { get; }

    public IReadOnlyList<WarpIrOpCode> Instructions => instructions;

    public IReadOnlyList<WarpReductionOperation> Reductions => reductions;

    public bool ExactlyMatches(WarpBackendContract other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return string.Equals(ProfileId, other.ProfileId, StringComparison.Ordinal) &&
            Instructions.SequenceEqual(other.Instructions) &&
            Reductions.SequenceEqual(other.Reductions);
    }

    private static void ValidateValues<T>(T[] values, string parameterName)
        where T : struct, Enum
    {
        if (values.Any(value => !Enum.IsDefined(value)))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "A backend contract contains an unregistered value.");
        }

        if (values.Distinct().Count() != values.Length)
        {
            throw new ArgumentException(
                "A backend contract cannot contain duplicate values.",
                parameterName);
        }
    }
}
