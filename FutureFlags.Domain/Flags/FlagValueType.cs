using FutureFlags.Domain.Shared;
using FutureFlags.Evaluation;

namespace FutureFlags.Domain.Flags;

/// <summary>
/// What kind of value a flag serves — OpenFeature's four flag types.
///
/// <para>
/// All four exist so the wire shape, the persisted event payload, and the ruleset carry a typed
/// value from today. Only <see cref="Boolean"/> is <see cref="IsAuthorable"/>, and
/// <see cref="Create"/> refuses the rest: nothing in this build can produce a string, number, or
/// object flag, and pretending otherwise would let a caller create a flag no evaluator could serve.
/// Turning one of the others on is a domain change, not a second breaking change to an event stream
/// that cannot be rolled back — which is the whole reason they are named here first.
/// </para>
/// <para>
/// No primary constructor, and it cannot have one: a primary constructor is at least as accessible
/// as its type, which would let a caller mint a value type that never went through
/// <see cref="Create"/>. Same rule as <see cref="EnvironmentKey"/> and <see cref="FlagKey"/>.
/// </para>
/// </summary>
public sealed record FlagValueType
{
    public const int MaxLength = 20;

    /// <summary>True or false. Every flag this build can author.</summary>
    public static readonly FlagValueType Boolean = new(FlagValueTypeNames.Boolean, 0, isAuthorable: true);

    /// <summary>A string. Named, not yet authorable.</summary>
    public static readonly FlagValueType String = new(FlagValueTypeNames.String, 1, isAuthorable: false);

    /// <summary>An IEEE-754 binary64 number. Named, not yet authorable.</summary>
    public static readonly FlagValueType Number = new(FlagValueTypeNames.Number, 2, isAuthorable: false);

    /// <summary>A JSON object or array. Named, not yet authorable.</summary>
    public static readonly FlagValueType Object = new(FlagValueTypeNames.Object, 3, isAuthorable: false);

    private FlagValueType(string value, int ordinal, bool isAuthorable)
    {
        Value = value;
        Ordinal = ordinal;
        IsAuthorable = isAuthorable;
    }

    /// <summary>The name as it appears on the wire and in storage.</summary>
    public string Value { get; }

    /// <summary>Position in <see cref="All"/>, on the same terms as <see cref="EnvironmentKey.Ordinal"/>.</summary>
    public int Ordinal { get; }

    /// <summary>Whether this build can create a flag of this type. Only boolean, for now.</summary>
    public bool IsAuthorable { get; }

    /// <summary>Every value type the wire can carry, authorable or not.</summary>
    public static IReadOnlyList<FlagValueType> All { get; } = [Boolean, String, Number, Object];

    /// <summary>
    /// The value type for a name a caller supplied. A name this build knows but cannot yet author
    /// is refused with its own error rather than with "unrecognized" — the distinction is the
    /// difference between a typo and a feature that has not shipped, and a caller deserves to be
    /// told which one they hit.
    /// </summary>
    public static Result<FlagValueType> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Success(Boolean);

        var normalized = value.Trim().ToLowerInvariant();
        var valueType = All.FirstOrDefault(candidate => candidate.Value == normalized);

        if (valueType is null)
            return Result.Failure<FlagValueType>(FlagErrors.ValueTypeUnrecognized(value));

        return valueType.IsAuthorable
            ? Result.Success(valueType)
            : Result.Failure<FlagValueType>(FlagErrors.ValueTypeNotSupported(valueType));
    }

    /// <summary>
    /// Rehydrates a value type already validated on its way into storage. For persistence use only,
    /// and unlike <see cref="Create"/> it accepts every type — a stream written by a later build
    /// must still replay here rather than throwing halfway through a history.
    /// </summary>
    public static FlagValueType FromPersisted(string? value) =>
        value is null
            ? Boolean
            : All.FirstOrDefault(candidate => candidate.Value == value)
              ?? throw new InvalidOperationException($"'{value}' is not a flag value type this application recognizes.");

    public override string ToString() => Value;
}
