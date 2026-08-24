using FeatureFlags.Domain.Shared;
using FeatureFlags.Evaluation;

namespace FeatureFlags.Domain.Segments;

/// <summary>
/// One test against a context attribute: "plan is one of pro or team", "accountAgeDays is at least
/// 30". A segment's conditions are ANDed, so each of these is a hurdle rather than an alternative.
/// </summary>
public sealed record SegmentCondition
{
    public const int MaxAttributeLength = 100;
    public const int MaxValues = 100;

    private SegmentCondition(string attribute, ConditionOperator @operator, IReadOnlyList<AttributeValue> values)
    {
        Attribute = attribute;
        Operator = @operator;
        Values = values;
    }

    /// <summary>The attribute's name, folded to lowercase the same way a context folds its own —
    /// so a segment written against <c>plan</c> matches an application that sends <c>Plan</c>.</summary>
    public string Attribute { get; }

    public ConditionOperator Operator { get; }

    /// <summary>What to compare against, deduplicated and ordered so that two conditions meaning
    /// the same thing are the same condition.</summary>
    public IReadOnlyList<AttributeValue> Values { get; }

    public static Result<SegmentCondition> Create(
        string? attribute,
        string? @operator,
        IEnumerable<AttributeValue>? values)
    {
        if (string.IsNullOrWhiteSpace(attribute))
            return Result.Failure<SegmentCondition>(SegmentErrors.AttributeRequired);

        var normalizedAttribute = FlagContext.NormaliseName(attribute);

        if (normalizedAttribute.Length > MaxAttributeLength)
            return Result.Failure<SegmentCondition>(SegmentErrors.AttributeTooLong);

        var operatorResult = ConditionOperator.Create(@operator);
        if (operatorResult.IsFailure)
            return Result.Failure<SegmentCondition>(operatorResult.Error);

        var chosen = operatorResult.Value;

        // Deduplicated and ordered before anything is counted, so that "pro, pro, pro" is one value
        // rather than three and cannot trip the single-valued check below on its own.
        var normalizedValues = (values ?? [])
            .Where(value => value is not null)
            .Distinct()
            .OrderBy(value => value, AttributeValue.CanonicalComparer)
            .ToList();

        if (normalizedValues.Count == 0)
            return Result.Failure<SegmentCondition>(SegmentErrors.ValuesRequired);

        if (normalizedValues.Count > MaxValues)
            return Result.Failure<SegmentCondition>(SegmentErrors.TooManyValues);

        if (!chosen.IsMultiValued && normalizedValues.Count > 1)
            return Result.Failure<SegmentCondition>(SegmentErrors.OperatorTakesOneValue(chosen));

        foreach (var value in normalizedValues)
        {
            if (!value.IsRepresentable)
                return Result.Failure<SegmentCondition>(SegmentErrors.ValueNotRepresentable(normalizedAttribute));

            if (!chosen.AcceptsKind(value.Kind))
                return Result.Failure<SegmentCondition>(SegmentErrors.ValueKindNotAccepted(chosen, value.Kind));
        }

        return Result.Success(new SegmentCondition(normalizedAttribute, chosen, normalizedValues));
    }

    /// <summary>
    /// Rehydrates a condition that has already been validated on its way into storage.
    /// For persistence use only — this deliberately bypasses <see cref="Create"/>, because an
    /// operator retired in a later release must still be readable rather than blowing up a replay.
    /// </summary>
    public static SegmentCondition FromPersisted(
        string attribute,
        ConditionOperator @operator,
        IReadOnlyList<AttributeValue> values) =>
        new(attribute, @operator, values);

    /// <summary>
    /// Value equality, spelled out. A record compares <see cref="IReadOnlyList{T}"/> members by
    /// reference, which would make two conditions holding the same values unequal — and every
    /// "did anything actually change" check in this aggregate rests on this comparison, so the
    /// default would quietly raise an event on every save.
    /// </summary>
    public bool Equals(SegmentCondition? other) =>
        other is not null
        && string.Equals(Attribute, other.Attribute, StringComparison.Ordinal)
        && Operator == other.Operator
        && Values.SequenceEqual(other.Values);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(Attribute, StringComparer.Ordinal);
        hash.Add(Operator);

        foreach (var value in Values)
            hash.Add(value);

        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"{Attribute} {Operator} [{string.Join(", ", Values.Select(value => value.ToCanonicalString()))}]";
}
