using FeatureFlags.Domain.Shared;
using FeatureFlags.Evaluation;

namespace FeatureFlags.Domain.Segments;

/// <summary>Which kinds of value an operator can be asked about.</summary>
[Flags]
public enum AcceptedKinds
{
    None = 0,
    Text = 1,
    Number = 2,
    Boolean = 4,
    Any = Text | Number | Boolean,
}

/// <summary>
/// How a condition compares a context attribute to its values.
///
/// <para>
/// A closed set in the <see cref="Environments.EnvironmentKey"/> / <see cref="SdkKeys.SdkKeyKind"/>
/// shape, and with no primary constructor for the same reason those have none: a primary
/// constructor is at least as accessible as its type, which would let a caller mint an operator
/// this build has never heard of and store it.
/// </para>
/// <para>
/// The evaluator does not use this type — it is compiled into the client package too and may not
/// depend on <see cref="Result"/>, so it works from the plain strings in
/// <see cref="ConditionOperatorNames"/>. This is the validating half, and one test
/// (<c>ConditionOperatorNamesAreInStepTests</c>) holds the two together.
/// </para>
/// </summary>
public sealed record ConditionOperator
{
    public const int MaxLength = 24;

    public static readonly ConditionOperator EqualTo =
        new(ConditionOperatorNames.EqualTo, AcceptedKinds.Any, multiValued: false);

    public static readonly ConditionOperator OneOf =
        new(ConditionOperatorNames.OneOf, AcceptedKinds.Any, multiValued: true);

    public static readonly ConditionOperator Contains =
        new(ConditionOperatorNames.Contains, AcceptedKinds.Text, multiValued: false);

    public static readonly ConditionOperator StartsWith =
        new(ConditionOperatorNames.StartsWith, AcceptedKinds.Text, multiValued: false);

    public static readonly ConditionOperator EndsWith =
        new(ConditionOperatorNames.EndsWith, AcceptedKinds.Text, multiValued: false);

    public static readonly ConditionOperator GreaterThan =
        new(ConditionOperatorNames.GreaterThan, AcceptedKinds.Number, multiValued: false);

    public static readonly ConditionOperator GreaterThanOrEqual =
        new(ConditionOperatorNames.GreaterThanOrEqual, AcceptedKinds.Number, multiValued: false);

    public static readonly ConditionOperator LessThan =
        new(ConditionOperatorNames.LessThan, AcceptedKinds.Number, multiValued: false);

    public static readonly ConditionOperator LessThanOrEqual =
        new(ConditionOperatorNames.LessThanOrEqual, AcceptedKinds.Number, multiValued: false);

    private ConditionOperator(string value, AcceptedKinds accepts, bool multiValued)
    {
        Value = value;
        Accepts = accepts;
        IsMultiValued = multiValued;
    }

    public string Value { get; }

    /// <summary>
    /// Which value kinds this operator can compare. A condition carrying a kind it does not accept
    /// is refused when it is authored, rather than saved and then silently never matching — the
    /// evaluator's "wrong type is a non-match" rule is the right answer at read time and the wrong
    /// answer at write time, where somebody is still in a position to fix it.
    /// </summary>
    public AcceptedKinds Accepts { get; }

    /// <summary>Whether more than one value is meaningful. A single-valued operator is refused a
    /// second value rather than quietly comparing against only the first.</summary>
    public bool IsMultiValued { get; }

    public static IReadOnlyList<ConditionOperator> All { get; } =
    [
        EqualTo,
        OneOf,
        Contains,
        StartsWith,
        EndsWith,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
    ];

    public static Result<ConditionOperator> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<ConditionOperator>(SegmentErrors.OperatorRequired);

        var normalized = value.Trim().ToLowerInvariant();
        var chosen = All.FirstOrDefault(candidate => candidate.Value == normalized);

        return chosen is null
            ? Result.Failure<ConditionOperator>(SegmentErrors.OperatorUnrecognized(value))
            : Result.Success(chosen);
    }

    /// <summary>Whether this operator can be asked about a value of this kind.</summary>
    public bool AcceptsKind(AttributeValueKind kind) => (Accepts & KindFlag(kind)) != AcceptedKinds.None;

    /// <summary>
    /// Rehydrates an operator that has already been validated on its way into storage.
    /// For persistence use only — this deliberately bypasses <see cref="Create"/>.
    /// </summary>
    public static ConditionOperator FromPersisted(string value) =>
        All.FirstOrDefault(candidate => candidate.Value == value)
        ?? throw new InvalidOperationException($"'{value}' is not a condition operator this application recognizes.");

    public override string ToString() => Value;

    private static AcceptedKinds KindFlag(AttributeValueKind kind) => kind switch
    {
        AttributeValueKind.Text => AcceptedKinds.Text,
        AttributeValueKind.Number => AcceptedKinds.Number,
        _ => AcceptedKinds.Boolean,
    };
}
