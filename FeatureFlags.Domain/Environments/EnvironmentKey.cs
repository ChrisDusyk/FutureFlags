using FeatureFlags.Domain.Shared;

namespace FeatureFlags.Domain.Environments;

/// <summary>
/// One of the places a flag is evaluated. There is no environment aggregate yet — the set is fixed
/// here and mirrored by <c>frontend/src/shell/environment.ts</c>, whose <c>key</c> values are these
/// strings. Changing one without the other breaks the console's requests, so change both together.
/// </summary>
public sealed record EnvironmentKey
{
    public const int MaxLength = 20;

    public static readonly EnvironmentKey Development = new("dev", 0);
    public static readonly EnvironmentKey Staging = new("stg", 1);
    public static readonly EnvironmentKey Production = new("prod", 2);

    private EnvironmentKey(string value, int ordinal)
    {
        Value = value;
        Ordinal = ordinal;
    }

    public string Value { get; }

    /// <summary>
    /// Position in <see cref="All"/> — the order a person moves through environments. Fixed at
    /// construction rather than searched for, since there are only ever these three instances: a
    /// caller sorting a list of flags or segments by environment does it once per row, and that
    /// should be a field read, not a linear scan through <c>All</c> — or worse, an
    /// <c>All.ToList().IndexOf(...)</c> that allocates a fresh list to throw away on every row.
    /// </summary>
    public int Ordinal { get; }

    /// <summary>Every environment a flag carries state for, in the order a person moves through them.</summary>
    public static IReadOnlyList<EnvironmentKey> All { get; } = [Development, Staging, Production];

    public static Result<EnvironmentKey> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<EnvironmentKey>(EnvironmentErrors.KeyRequired);

        var normalized = value.Trim().ToLowerInvariant();
        var environment = All.FirstOrDefault(candidate => candidate.Value == normalized);

        return environment is null
            ? Result.Failure<EnvironmentKey>(EnvironmentErrors.KeyUnrecognized(value))
            : Result.Success(environment);
    }

    /// <summary>
    /// Rehydrates an environment that has already been validated on its way into storage.
    /// For persistence use only — this deliberately bypasses <see cref="Create"/>.
    /// </summary>
    public static EnvironmentKey FromPersisted(string value) =>
        All.FirstOrDefault(candidate => candidate.Value == value)
        ?? throw new InvalidOperationException($"'{value}' is not an environment this application recognizes.");

    public override string ToString() => Value;
}
