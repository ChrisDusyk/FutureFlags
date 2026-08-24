using FeatureFlags.Domain.Environments;
using FeatureFlags.Domain.Shared;

namespace FeatureFlags.Domain.Flags;

public static class FlagErrors
{
    public static Error KeyRequired => Error.Validation(
        "Flag.Key.Required",
        "A flag key is required.");

    public static Error KeyTooLong => Error.Validation(
        "Flag.Key.TooLong",
        $"A flag key must be {FlagKey.MaxLength} characters or fewer.");

    public static Error KeyInvalidFormat => Error.Validation(
        "Flag.Key.InvalidFormat",
        "A flag key must be a lowercase slug containing only letters, digits, and single hyphens between segments.");

    public static Error NameRequired => Error.Validation(
        "Flag.Name.Required",
        "A flag name is required.");

    public static Error NameTooLong => Error.Validation(
        "Flag.Name.TooLong",
        $"A flag name must be {FeatureFlag.MaxNameLength} characters or fewer.");

    public static Error DescriptionTooLong => Error.Validation(
        "Flag.Description.TooLong",
        $"A flag description must be {FeatureFlag.MaxDescriptionLength} characters or fewer.");

    public static Error TooManyTargetedSegments(FlagKey key) => Error.Validation(
        "Flag.TooManyTargetedSegments",
        $"The flag '{key}' can target at most {FeatureFlag.MaxTargetedSegments} segments in one environment.");

    public static Error DuplicateKey(FlagKey key) => Error.Conflict(
        "Flag.DuplicateKey",
        $"A flag with the key '{key}' already exists.");

    /// <summary>Another writer appended an event for this flag between this caller's read and its
    /// write — the store's own sequence-number check caught it, so the caller must reload.</summary>
    public static Error ConcurrencyConflict(FlagKey key) => Error.Conflict(
        "Flag.ConcurrencyConflict",
        $"The flag '{key}' was changed by someone else. Reload and try again.");

    public static Error NotFound(FlagKey key) => Error.NotFound(
        "Flag.NotFound",
        $"No flag with the key '{key}' exists.");

    /// <summary>
    /// A flag that carries no state for an environment. Unreachable while the environment set is
    /// fixed, and a genuine bug rather than a caller's mistake if it ever is reached.
    /// </summary>
    public static Error StateMissing(FlagKey key, EnvironmentKey environment) => Error.Failure(
        "Flag.StateMissing",
        $"The flag '{key}' carries no state for the '{environment}' environment.");
}
