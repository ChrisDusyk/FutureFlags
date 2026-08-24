using System;
using FeatureFlags.Evaluation;

namespace FeatureFlags.Client.Internal;

/// <summary>
/// One version of the ruleset, whole.
///
/// <para>
/// Immutable, and replaced rather than mutated: a reader takes the current reference and works
/// from it, so a refresh landing mid-read cannot show it half of one version and half of another.
/// That is also what lets reads take no lock at all.
/// </para>
/// <para>
/// It holds the ruleset rather than a map of answers, because this package now evaluates for
/// itself. A key-and-boolean snapshot could only ever answer for nobody in particular, which is the
/// one question a client with a user in front of it is not asking.
/// </para>
/// </summary>
internal sealed class FlagSnapshot(Ruleset ruleset, string? etag, DateTimeOffset fetchedAt)
{
    /// <summary>The flags and the segments they reach, as the server sent them.</summary>
    public Ruleset Ruleset { get; } = ruleset;

    /// <summary>The environment the SDK key is scoped to, as the server reported it.</summary>
    public string Environment => Ruleset.Environment;

    /// <summary>What to send as <c>If-None-Match</c> next time, so an unchanged poll costs a 304.</summary>
    public string? ETag { get; } = etag;

    public DateTimeOffset FetchedAt { get; } = fetchedAt;

    /// <summary>The same answer, re-stamped. What a 304 produces.</summary>
    public FlagSnapshot RefreshedAt(DateTimeOffset timestamp) => new(Ruleset, ETag, timestamp);
}
