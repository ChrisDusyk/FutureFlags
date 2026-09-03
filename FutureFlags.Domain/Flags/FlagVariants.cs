using FutureFlags.Domain.Shared;
using FutureFlags.Evaluation;

namespace FutureFlags.Domain.Flags;

/// <summary>One named value a flag can serve.</summary>
/// <param name="Name">The variant's name, as a resolution reports it.</param>
/// <param name="Value">What the flag serves when this variant is chosen.</param>
public sealed record FlagVariant(string Name, FlagValue Value);

/// <summary>
/// The named values a flag can serve, in a normal form.
///
/// <para>
/// Deduplicated by name and ordinal-ordered, on the same terms as <see cref="SegmentDefinition"/>
/// and for the same reason: every idempotence check in <see cref="FeatureFlag"/> compares one of
/// these against another, so two spellings of the same set have to be equal. Equality is
/// hand-written over the sequence because a record would compare the list by reference and every
/// one of those checks would silently start raising an event on each save.
/// </para>
/// <para>
/// A boolean flag's set is exactly <c>{ on: true, off: false }</c> and <see cref="Create"/> refuses
/// anything else. That is not a placeholder for a weaker rule later — a boolean flag with three
/// variants, or with <c>on</c> mapped to <c>false</c>, is a flag whose answer no reader could
/// predict from its name.
/// </para>
/// </summary>
public sealed record FlagVariants
{
    /// <summary>
    /// How many variants one flag may carry. A ceiling rather than a considered limit, on the same
    /// terms as <see cref="FeatureFlag.MaxTargetedSegments"/> — the ruleset ships every variant, so
    /// an unbounded set is an unbounded payload.
    /// </summary>
    public const int MaxVariants = 25;

    public const int MaxNameLength = 100;

    private FlagVariants(IReadOnlyList<FlagVariant> variants) => Variants = variants;

    /// <summary>The variants, deduplicated by name and ordinal-ordered.</summary>
    public IReadOnlyList<FlagVariant> Variants { get; }

    /// <summary>
    /// The two variants every boolean flag has. Ordinal order puts <c>off</c> before <c>on</c>,
    /// which reads oddly and is correct — the order is the normal form, not a presentation choice.
    /// </summary>
    public static FlagVariants BooleanPair { get; } = new(
    [
        new FlagVariant(FlagVariantNames.Off, FlagValue.False),
        new FlagVariant(FlagVariantNames.On, FlagValue.True),
    ]);

    /// <summary>
    /// The variant set for a flag of this type. A null or empty set means the default for the type,
    /// which is how every caller in this build creates a flag.
    /// </summary>
    public static Result<FlagVariants> Create(FlagValueType valueType, IEnumerable<FlagVariant>? variants)
    {
        var supplied = (variants ?? []).Where(variant => variant is not null).ToList();

        if (supplied.Count == 0)
            return valueType == FlagValueType.Boolean
                ? Result.Success(BooleanPair)
                : Result.Failure<FlagVariants>(FlagErrors.VariantsRequired(valueType));

        if (supplied.Count > MaxVariants)
            return Result.Failure<FlagVariants>(FlagErrors.TooManyVariants);

        foreach (var variant in supplied)
        {
            if (string.IsNullOrWhiteSpace(variant.Name))
                return Result.Failure<FlagVariants>(FlagErrors.VariantNameRequired);

            if (variant.Name.Trim().Length > MaxNameLength)
                return Result.Failure<FlagVariants>(FlagErrors.VariantNameTooLong);

            if (variant.Value is null || !variant.Value.IsRepresentable)
                return Result.Failure<FlagVariants>(FlagErrors.VariantValueNotRepresentable(variant.Name));
        }

        var normalized = supplied
            .Select(variant => new FlagVariant(variant.Name.Trim(), variant.Value))
            .DistinctBy(variant => variant.Name, StringComparer.Ordinal)
            .OrderBy(variant => variant.Name, StringComparer.Ordinal)
            .ToList();

        var candidate = new FlagVariants(normalized);

        // A boolean flag's set is the boolean pair or it is a flag nobody can read. Compared as a
        // whole rather than name by name, so a set with the right names and swapped values is
        // refused too.
        if (valueType == FlagValueType.Boolean && candidate != BooleanPair)
            return Result.Failure<FlagVariants>(FlagErrors.BooleanVariantsFixed);

        return Result.Success(candidate);
    }

    /// <summary>
    /// Rehydrates a set already validated on its way into storage. For persistence use only — it
    /// deliberately bypasses <see cref="Create"/>, so a stream written by a later build replays
    /// here rather than failing halfway through a history.
    /// </summary>
    public static FlagVariants FromPersisted(IEnumerable<FlagVariant>? variants)
    {
        var supplied = (variants ?? [])
            .Where(variant => variant is not null)
            .DistinctBy(variant => variant.Name, StringComparer.Ordinal)
            .OrderBy(variant => variant.Name, StringComparer.Ordinal)
            .ToList();

        return supplied.Count == 0 ? BooleanPair : new FlagVariants(supplied);
    }

    /// <summary>Whether a variant by this name exists.</summary>
    public bool Contains(string? name) =>
        name is not null && Variants.Any(variant => string.Equals(variant.Name, name, StringComparison.Ordinal));

    /// <summary>The value behind a name, or <see cref="Option{T}.None"/> when nothing carries it.</summary>
    public Option<FlagValue> ValueOf(string? name) =>
        Variants.FirstOrDefault(variant => string.Equals(variant.Name, name, StringComparison.Ordinal))?.Value.ToOption()
        ?? Option<FlagValue>.None;

    /// <summary>The set as the ruleset carries it, name to value.</summary>
    public IReadOnlyDictionary<string, FlagValue> ToDictionary()
    {
        var index = new Dictionary<string, FlagValue>(Variants.Count, StringComparer.Ordinal);

        foreach (var variant in Variants)
            index[variant.Name] = variant.Value;

        return index;
    }

    /// <summary>
    /// Value equality over the sequence. See <see cref="SegmentDefinition.Equals(SegmentDefinition)"/>
    /// — a record would compare the list by reference, and every idempotence check depends on it
    /// not doing that.
    /// </summary>
    public bool Equals(FlagVariants? other) =>
        other is not null && Variants.SequenceEqual(other.Variants);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var variant in Variants)
            hash.Add(variant);

        return hash.ToHashCode();
    }
}
