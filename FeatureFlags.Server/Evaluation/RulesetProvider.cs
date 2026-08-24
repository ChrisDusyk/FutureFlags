using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using FeatureFlags.Domain.Environments;
using FeatureFlags.Domain.Flags;
using FeatureFlags.Domain.Segments;
using FeatureFlags.Evaluation;
using Microsoft.Extensions.Caching.Hybrid;

namespace FeatureFlags.Server.Evaluation;

/// <summary>
/// One environment's flags and the segments they reach, cached, with the tag that identifies that
/// exact version of it.
///
/// <para>
/// Deliberately not inside a feature slice. Three routes read it — <c>GET /api/evaluation</c>,
/// <c>GET /api/evaluation/ruleset</c>, and <c>POST /api/evaluation</c> — and giving each its own
/// copy would mean three cache keys, three lifetimes, and three chances for the answers to differ
/// while claiming to describe the same environment. It sits next to <c>Api/</c> and <c>Hosting/</c>
/// as server-level plumbing rather than behaviour any one slice owns.
/// </para>
/// <para>
/// The read path a fleet of application servers sits on, so it is still the one place here that
/// caches, and the bargain is unchanged: <see cref="CacheLifetime"/> of staleness after a write, in
/// exchange for a poll that mostly does not reach Postgres. The console reads the admin listing
/// instead, so what an operator sees after flipping a switch is always the truth.
/// </para>
/// </summary>
public sealed class RulesetProvider(
    IFlagViewRepository flags,
    ISegmentViewRepository segments,
    HybridCache cache)
{
    /// <summary>
    /// Short enough that nobody reasons about it, long enough to absorb a fleet's poll. Inherited
    /// unchanged from when this cached answers rather than definitions, because it is the number
    /// <c>clients/README.md</c> documents to anyone choosing a polling interval.
    /// </summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(5);

    public async ValueTask<CachedRuleset> GetAsync(
        EnvironmentKey environment,
        CancellationToken cancellationToken = default) =>
        await cache.GetOrCreateAsync(
            CacheKeyFor(environment),
            (Flags: flags, Segments: segments, Environment: environment),
            static async (state, token) =>
            {
                var allFlags = await state.Flags.ListAsync(token);
                var allSegments = await state.Segments.ListAsync(token);

                return Build(allFlags, allSegments, state.Environment);
            },
            new HybridCacheEntryOptions
            {
                Expiration = CacheLifetime,
                LocalCacheExpiration = CacheLifetime,
            },
            cancellationToken: cancellationToken);

    /// <summary>
    /// Versioned, because this key used to hold a different payload. During a rolling deploy an old
    /// pod sharing the same Redis would otherwise deserialize the new shape as garbage.
    /// </summary>
    private static string CacheKeyFor(EnvironmentKey environment) => $"ruleset:v1:{environment.Value}";

    /// <summary>
    /// Builds the ruleset and its tag together, from the same ordered pass, so the tag cannot
    /// describe a payload a caller did not get.
    ///
    /// <para>
    /// Public and static because it is a pure projection with no cache and no repository in it, and
    /// the properties worth pinning down — the ordering, what is reachable, and that two different
    /// rulesets cannot share a tag — are all properties of this function alone.
    /// </para>
    /// </summary>
    public static CachedRuleset Build(
        IReadOnlyList<FlagView> flags,
        IReadOnlyList<SegmentView> segments,
        EnvironmentKey environment)
    {
        // Ordered explicitly rather than relying on the repositories' ordering: a tag is only
        // meaningful if the same set of facts always hashes the same way.
        var rulesetFlags = flags
            .Select(flag => flag.StateIn(environment).Match(
                state => new RulesetFlag(
                    flag.Key.Value,
                    state.IsEnabled,
                    [.. state.TargetedSegments.Select(segment => segment.Value).OrderBy(key => key, StringComparer.Ordinal)]),
                // A flag with no state for an environment cannot happen while the set is fixed.
                // Off and reaching nobody is the safe reading of a fact that is missing.
                () => new RulesetFlag(flag.Key.Value, false, [])))
            .OrderBy(flag => flag.Key, StringComparer.Ordinal)
            .ToList();

        // Only what is reachable. The one way an engine can arrive at a segment is through some
        // flag's targeting, so shipping the rest would be a larger body, an ETag that moves when an
        // unrelated segment is edited, and every segment's definition disclosed to a key scoped to
        // an environment that never uses it.
        var reachable = rulesetFlags
            .SelectMany(flag => flag.TargetedSegments)
            .ToHashSet(StringComparer.Ordinal);

        var rulesetSegments = segments
            .Where(segment => reachable.Contains(segment.Key.Value))
            .Select(ToRulesetSegment)
            .OrderBy(segment => segment.Key, StringComparer.Ordinal)
            .ToList();

        var ruleset = new Ruleset(environment.Value, rulesetFlags, rulesetSegments);

        return new CachedRuleset(ruleset, Fingerprint(ruleset));
    }

    private static RulesetSegment ToRulesetSegment(SegmentView segment) => new(
        segment.Key.Value,
        segment.Definition.IncludedKeys,
        segment.Definition.ExcludedKeys,
        [.. segment.Definition.Conditions.Select(condition => new RulesetCondition(
            condition.Attribute,
            condition.Operator.Value,
            condition.Values))]);

    /// <summary>
    /// A tag over everything in the payload.
    ///
    /// <para>
    /// Every part is length-prefixed. Without that, two different rulesets can be made to render
    /// the same bytes — an attribute named <c>ab</c> holding <c>c</c> against one named <c>a</c>
    /// holding <c>bc</c> — and a caller would then be told nothing had changed when it had.
    /// </para>
    /// </summary>
    private static string Fingerprint(Ruleset ruleset)
    {
        var fingerprint = new StringBuilder();

        Append(fingerprint, "ffruleset/1");
        Append(fingerprint, ruleset.Environment);

        foreach (var flag in ruleset.Flags)
        {
            Append(fingerprint, flag.Key);
            fingerprint.Append(flag.IsEnabled ? "=1\n" : "=0\n");

            foreach (var segment in flag.TargetedSegments)
                Append(fingerprint, segment);
        }

        foreach (var segment in ruleset.Segments)
        {
            Append(fingerprint, segment.Key);

            foreach (var included in segment.Included)
                Append(fingerprint, "+" + included);

            foreach (var excluded in segment.Excluded)
                Append(fingerprint, "-" + excluded);

            foreach (var condition in segment.Conditions)
            {
                Append(fingerprint, condition.Attribute);
                Append(fingerprint, condition.Operator);

                foreach (var value in condition.Values)
                    Append(fingerprint, value.ToCanonicalString());
            }
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString()));

        // Quoted because an ETag is a quoted-string on the wire, and a bare one is silently ignored
        // by anything that reads the header properly.
        return $"\"{Base64Url.EncodeToString(hash)}\"";
    }

    private static void Append(StringBuilder fingerprint, string part) =>
        fingerprint.Append(part.Length).Append(':').Append(part).Append('\n');
}

/// <summary>The ruleset and the tag that identifies this exact version of it.</summary>
public sealed record CachedRuleset(Ruleset Ruleset, string ETag);
