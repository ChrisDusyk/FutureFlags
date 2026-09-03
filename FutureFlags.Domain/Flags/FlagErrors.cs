using FutureFlags.Domain.Environments;
using FutureFlags.Domain.Shared;
using FutureFlags.Evaluation;

namespace FutureFlags.Domain.Flags;

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

    public static Error ValueTypeUnrecognized(string value) => Error.Validation(
        "Flag.ValueType.Unrecognized",
        $"'{value}' is not a flag value type. Valid types are: {string.Join(", ", FlagValueType.All.Select(type => type.Value))}.");

    /// <summary>
    /// A value type this build knows about but cannot yet author. Deliberately separate from
    /// <see cref="ValueTypeUnrecognized"/>: one is a typo and the other is a feature that has not
    /// shipped, and a caller can only act on the difference if we tell them which they hit.
    /// </summary>
    public static Error ValueTypeNotSupported(FlagValueType valueType) => Error.Validation(
        "Flag.ValueType.NotSupported",
        $"Flags of type '{valueType}' are not supported yet. Only '{FlagValueType.Boolean}' flags can be created.");

    public static Error VariantsRequired(FlagValueType valueType) => Error.Validation(
        "Flag.Variants.Required",
        $"A flag of type '{valueType}' must name at least one variant.");

    public static Error TooManyVariants => Error.Validation(
        "Flag.Variants.TooMany",
        $"A flag can carry at most {FlagVariants.MaxVariants} variants.");

    public static Error VariantNameRequired => Error.Validation(
        "Flag.Variants.NameRequired",
        "Every variant needs a name.");

    public static Error VariantNameTooLong => Error.Validation(
        "Flag.Variants.NameTooLong",
        $"A variant name must be {FlagVariants.MaxNameLength} characters or fewer.");

    public static Error VariantValueNotRepresentable(string name) => Error.Validation(
        "Flag.Variants.ValueNotRepresentable",
        $"The value of variant '{name}' cannot be represented in every runtime that evaluates it.");

    /// <summary>
    /// A boolean flag's variants are exactly <c>on</c> and <c>off</c>, mapped to true and false. A
    /// set with the right names and swapped values is refused by this too.
    /// </summary>
    public static Error BooleanVariantsFixed => Error.Validation(
        "Flag.Variants.BooleanFixed",
        $"A boolean flag's variants are exactly '{FlagVariantNames.On}' (true) and '{FlagVariantNames.Off}' (false).");

    /// <summary>A state naming a variant the flag does not carry — it would resolve to nothing.</summary>
    public static Error VariantUnknown(FlagKey key, string name) => Error.Validation(
        "Flag.Variants.Unknown",
        $"The flag '{key}' carries no variant named '{name}'.");

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
