using FeatureFlags.Domain.Shared;

namespace FeatureFlags.Domain.Segments;

/// <summary>
/// Who is in a segment: two lists of context keys, and a set of conditions that all have to hold.
///
/// <para>
/// The reading order is fixed and it is <see cref="Evaluation.SegmentMatcher"/> that states it —
/// excluded beats included beats conditions. This type's job is only to make sure a definition that
/// reaches the matcher is one the matcher can answer for, and to put it in a normal form.
/// </para>
/// <para>
/// That normal form is load-bearing rather than tidiness. A definition is a nested structure, so
/// "did this actually change" has no cheap answer unless two definitions meaning the same thing are
/// equal — and without that, the console re-posting an unchanged form raises an event, moves the
/// timestamp, and churns every SDK's ETag every time somebody opens the editor and saves.
/// </para>
/// </summary>
public sealed record SegmentDefinition
{
    public const int MaxKeys = 1000;
    public const int MaxConditions = 25;
    public const int MaxKeyLength = 256;

    private SegmentDefinition(
        IReadOnlyList<string> includedKeys,
        IReadOnlyList<string> excludedKeys,
        IReadOnlyList<SegmentCondition> conditions)
    {
        IncludedKeys = includedKeys;
        ExcludedKeys = excludedKeys;
        Conditions = conditions;
    }

    /// <summary>Context keys in this segment whatever the conditions say. Deduplicated, ordinal-sorted.</summary>
    public IReadOnlyList<string> IncludedKeys { get; }

    /// <summary>Context keys out of it whatever anything else says. Deduplicated, ordinal-sorted.</summary>
    public IReadOnlyList<string> ExcludedKeys { get; }

    /// <summary>
    /// All of these must hold. Deduplicated but kept in the order they were written: the order has
    /// no effect on the answer, and preserving it keeps the editor and the history diff readable.
    /// </summary>
    public IReadOnlyList<SegmentCondition> Conditions { get; }

    /// <summary>
    /// A definition that admits nobody. Not the same as "no restrictions" — see
    /// <see cref="Evaluation.SegmentMatcher.Matches"/> for why an empty definition matches nobody
    /// rather than everybody.
    /// </summary>
    public static SegmentDefinition Empty { get; } = new([], [], []);

    /// <summary>
    /// Whether this definition has nothing in it: no included keys and no conditions. The one
    /// case guaranteed to match nobody — not the only one. A definition whose conditions happen
    /// to be mutually exclusive (two <c>equals</c> conditions on the same attribute demanding
    /// different values, say) also matches nobody, but detecting that in general is a
    /// satisfiability problem this checks makes no attempt at; this is a structural read of the
    /// definition, not a proof about what it can match.
    /// </summary>
    public bool IsEmpty => IncludedKeys.Count == 0 && Conditions.Count == 0;

    public static Result<SegmentDefinition> Create(
        IEnumerable<string>? includedKeys,
        IEnumerable<string>? excludedKeys,
        IEnumerable<SegmentCondition>? conditions)
    {
        var included = NormalizeKeys(includedKeys);
        if (included.IsFailure)
            return Result.Failure<SegmentDefinition>(included.Error);

        var excluded = NormalizeKeys(excludedKeys);
        if (excluded.IsFailure)
            return Result.Failure<SegmentDefinition>(excluded.Error);

        // Distinct, not sorted: two identical conditions are one condition, but the order the author
        // chose is information a reordering would throw away.
        var normalizedConditions = (conditions ?? [])
            .Where(condition => condition is not null)
            .Distinct()
            .ToList();

        if (normalizedConditions.Count > MaxConditions)
            return Result.Failure<SegmentDefinition>(SegmentErrors.TooManyConditions);

        return Result.Success(new SegmentDefinition(included.Value, excluded.Value, normalizedConditions));
    }

    /// <summary>
    /// Rehydrates a definition that has already been validated on its way into storage.
    /// For persistence use only — this deliberately bypasses <see cref="Create"/>.
    /// </summary>
    public static SegmentDefinition FromPersisted(
        IReadOnlyList<string> includedKeys,
        IReadOnlyList<string> excludedKeys,
        IReadOnlyList<SegmentCondition> conditions) =>
        new(includedKeys, excludedKeys, conditions);

    private static Result<IReadOnlyList<string>> NormalizeKeys(IEnumerable<string>? keys)
    {
        var normalized = new List<string>();

        foreach (var key in keys ?? [])
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var trimmed = key.Trim();

            // Not lowercased, unlike a flag or segment key. This is somebody else's identifier —
            // a user id, an account id — and folding its case would silently target the wrong row.
            if (trimmed.Length > MaxKeyLength)
                return Result.Failure<IReadOnlyList<string>>(SegmentErrors.ContextKeyTooLong);

            normalized.Add(trimmed);
        }

        var distinct = normalized
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        if (distinct.Count > MaxKeys)
            return Result.Failure<IReadOnlyList<string>>(SegmentErrors.TooManyKeys);

        return Result.Success<IReadOnlyList<string>>(distinct);
    }

    /// <summary>
    /// Value equality over the three lists. See <see cref="SegmentCondition.Equals(SegmentCondition)"/>
    /// — a record would compare these by reference, and every idempotence check here depends on it
    /// not doing that.
    /// </summary>
    public bool Equals(SegmentDefinition? other) =>
        other is not null
        && IncludedKeys.SequenceEqual(other.IncludedKeys, StringComparer.Ordinal)
        && ExcludedKeys.SequenceEqual(other.ExcludedKeys, StringComparer.Ordinal)
        && Conditions.SequenceEqual(other.Conditions);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var key in IncludedKeys)
            hash.Add(key, StringComparer.Ordinal);

        foreach (var key in ExcludedKeys)
            hash.Add(key, StringComparer.Ordinal);

        foreach (var condition in Conditions)
            hash.Add(condition);

        return hash.ToHashCode();
    }
}
