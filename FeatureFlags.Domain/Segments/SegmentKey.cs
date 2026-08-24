using System.Text.RegularExpressions;
using FeatureFlags.Domain.Shared;

namespace FeatureFlags.Domain.Segments;

/// <summary>
/// The stable, human-readable identifier for a segment (e.g. "beta-testers"). Normalized to a
/// lowercase slug so that "Beta-Testers" and "beta-testers " are the same segment.
///
/// <para>
/// Deliberately the same shape and the same alphabet as <see cref="Flags.FlagKey"/>: both end up as
/// bare strings in a ruleset payload, and a reader should not have to remember which kind of key
/// allows which characters.
/// </para>
/// </summary>
public sealed partial record SegmentKey
{
    public const int MaxLength = 100;

    private SegmentKey(string value) => Value = value;

    public string Value { get; }

    public static Result<SegmentKey> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<SegmentKey>(SegmentErrors.KeyRequired);

        var normalized = value.Trim().ToLowerInvariant();

        if (normalized.Length > MaxLength)
            return Result.Failure<SegmentKey>(SegmentErrors.KeyTooLong);

        if (!SlugPattern().IsMatch(normalized))
            return Result.Failure<SegmentKey>(SegmentErrors.KeyInvalidFormat);

        return Result.Success(new SegmentKey(normalized));
    }

    /// <summary>
    /// Rehydrates a key that has already been validated on its way into storage.
    /// For persistence use only — this deliberately bypasses <see cref="Create"/>.
    /// </summary>
    public static SegmentKey FromPersisted(string value) => new(value);

    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
