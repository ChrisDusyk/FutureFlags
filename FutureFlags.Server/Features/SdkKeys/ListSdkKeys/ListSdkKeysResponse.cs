using FutureFlags.Domain.SdkKeys;

namespace FutureFlags.Server.Features.SdkKeys.ListSdkKeys;

/// <summary>
/// An SDK key as the console shows it. There is no field here that could be assembled back into a
/// working credential — <see cref="Hint"/> is the public selector, which identifies a key without
/// authenticating as one.
/// </summary>
public sealed record SdkKeySummary(
    Guid Id,
    string Name,
    string Kind,
    string Environment,
    string Hint,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt,
    bool IsActive)
{
    public static SdkKeySummary From(SdkKey key) => new(
        key.Id,
        key.Name,
        key.Kind.Value,
        key.Environment.Value,
        // Enough of the token to recognise it against a configuration file, and nothing more.
        $"{key.Kind.TokenPrefix}_{key.Environment.Value}_{key.Selector}",
        key.CreatedAt,
        // The wire has null for absent, which is what an Option resolves to at this boundary — a
        // JSON field that is sometimes missing is harder for a client than one that is sometimes null.
        key.LastUsedAt.Match(used => (DateTimeOffset?)used, () => null),
        key.RevokedAt.Match(revoked => (DateTimeOffset?)revoked, () => null),
        key.IsActive);
}

public sealed record ListSdkKeysResponse(IReadOnlyList<SdkKeySummary> Keys);
