using System.Text.RegularExpressions;
using FutureFlags.Domain.Shared;

namespace FutureFlags.Domain.Flags;

/// <summary>
/// The stable, human-readable identifier a client uses to evaluate a flag (e.g. "new-checkout").
/// Normalized to a lowercase slug so that "New-Checkout" and "new-checkout " are the same key.
/// </summary>
public sealed partial record FlagKey
{
    public const int MaxLength = 100;

    private FlagKey(string value) => Value = value;

    public string Value { get; }

    public static Result<FlagKey> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<FlagKey>(FlagErrors.KeyRequired);

        var normalized = value.Trim().ToLowerInvariant();

        if (normalized.Length > MaxLength)
            return Result.Failure<FlagKey>(FlagErrors.KeyTooLong);

        if (!SlugPattern().IsMatch(normalized))
            return Result.Failure<FlagKey>(FlagErrors.KeyInvalidFormat);

        return Result.Success(new FlagKey(normalized));
    }

    /// <summary>
    /// Rehydrates a key that has already been validated on its way into storage.
    /// For persistence use only — this deliberately bypasses <see cref="Create"/>.
    /// </summary>
    public static FlagKey FromPersisted(string value) => new(value);

    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
