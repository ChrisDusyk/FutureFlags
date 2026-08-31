using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FutureFlags.Evaluation;

/// <summary>
/// Everything needed to answer "is this flag on for this person" in one environment, exactly as it
/// travels on the wire from <c>GET /api/evaluation/ruleset</c>.
///
/// <para>
/// These are the types the evaluator works on — not the server's <c>FlagView</c>, not the client's
/// snapshot. That is deliberate and it is what makes the split honest: the server's contextual
/// endpoint and an SDK evaluating locally run <em>the same code over the identical JSON</em>, so
/// there is no shape in between for the two to disagree about.
/// </para>
/// <para>
/// It is also what the server caches. The domain's value objects have private constructors and
/// get-only properties, so <c>System.Text.Json</c> cannot rehydrate them — a ruleset cached as
/// domain objects would pass a test using only the in-memory cache tier and fail against Redis.
/// Caching the wire shape removes that whole class of surprise, because the cached thing and the
/// serialised thing are one type.
/// </para>
/// </summary>
public sealed class Ruleset(
    string environment,
    IReadOnlyList<RulesetFlag> flags,
    IReadOnlyList<RulesetSegment> segments)
{
    /// <summary>The environment this ruleset describes, as the server reported it.</summary>
    public string Environment { get; } = environment;

    /// <summary>Ordered by key, ordinal. An array rather than the key-to-boolean map that
    /// <c>GET /api/evaluation</c> answers with: an entry carries three facts now, and a stable
    /// order is what lets the ETag mean anything.</summary>
    public IReadOnlyList<RulesetFlag> Flags { get; } = flags ?? [];

    /// <summary>Ordered by key, ordinal. Only the segments some flag in this environment targets —
    /// nothing else is reachable, and shipping the rest would disclose definitions to a key that
    /// can never evaluate them.</summary>
    public IReadOnlyList<RulesetSegment> Segments { get; } = segments ?? [];

    // Memoized rather than rebuilt per call. A Ruleset is immutable and always replaced wholesale
    // on the next poll rather than mutated, so a cached index can never go stale under it — and
    // SegmentsByKey sits on every single-flag evaluation, called once per IsEnabledAsync and,
    // before this, once per flag inside GET /api/evaluation's own flattening loop. Racing threads
    // computing this once each and overwriting the field is fine: every computation of it from the
    // same instance is equal, so there is nothing to lock.
    private IReadOnlyDictionary<string, RulesetSegment>? _segmentsByKey;

    /// <summary>The segments by key, for the evaluator. Ordinal, because a segment key is a slug
    /// that was already lowercased on its way in.</summary>
    public IReadOnlyDictionary<string, RulesetSegment> SegmentsByKey() =>
        _segmentsByKey ??= BuildSegmentsByKey();

    private IReadOnlyDictionary<string, RulesetSegment> BuildSegmentsByKey()
    {
        var index = new Dictionary<string, RulesetSegment>(Segments.Count, StringComparer.Ordinal);

        foreach (var segment in Segments)
        {
            if (segment?.Key is not null)
                index[segment.Key] = segment;
        }

        return index;
    }
}

/// <summary>One flag's state in the environment, and who it reaches.</summary>
public sealed class RulesetFlag(string key, bool isEnabled, IReadOnlyList<string> targetedSegments)
{
    /// <summary>The flag's key, lowercase.</summary>
    public string Key { get; } = key;

    /// <summary>Whether the flag is on at all here. Off beats every segment.</summary>
    public bool IsEnabled { get; } = isEnabled;

    /// <summary>
    /// The segments this flag reaches in this environment. Empty means "everyone", which is what a
    /// flag meant before segments existed and is why this is additive rather than breaking.
    /// </summary>
    public IReadOnlyList<string> TargetedSegments { get; } = targetedSegments ?? [];
}

/// <summary>One segment's definition — the named group a flag can point at.</summary>
public sealed class RulesetSegment(
    string key,
    IReadOnlyList<string> included,
    IReadOnlyList<string> excluded,
    IReadOnlyList<RulesetCondition> conditions)
{
    /// <summary>The segment's key, lowercase.</summary>
    public string Key { get; } = key;

    /// <summary>Context keys that are in this segment whatever the conditions say.</summary>
    public IReadOnlyList<string> Included { get; } = included ?? [];

    /// <summary>Context keys that are out of it whatever anything else says.</summary>
    public IReadOnlyList<string> Excluded { get; } = excluded ?? [];

    /// <summary>All of these must hold. An empty list is not "everyone" — see
    /// <see cref="SegmentMatcher"/>.</summary>
    public IReadOnlyList<RulesetCondition> Conditions { get; } = conditions ?? [];
}

/// <summary>One test against a context attribute. See <see cref="SegmentMatcher.Satisfies"/>.</summary>
public sealed class RulesetCondition(string attribute, string @operator, IReadOnlyList<AttributeValue> values)
{
    /// <summary>Lowercase, matching how a context's attribute names are folded.</summary>
    public string Attribute { get; } = attribute;

    /// <summary>One of <see cref="ConditionOperatorNames"/>. An operator this build does not know is
    /// a non-match, not an error — a client one release behind must not start throwing.</summary>
    public string Operator { get; } = @operator;

    /// <summary>What to compare the attribute against. Empty matches nothing.</summary>
    public IReadOnlyList<AttributeValue> Values { get; } = values ?? [];
}

/// <summary>
/// The serializer settings for everything above, in one place so the server writing a ruleset and
/// an SDK reading it cannot drift. Both sides use this rather than their own options.
/// </summary>
public static class RulesetJson
{
    /// <summary>The settings both sides serialize and deserialize a ruleset with.</summary>
    public static JsonSerializerOptions Options { get; } = Build();

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        // AttributeValue carries its own converter as an attribute, so there is nothing to
        // register here — see the note on that type for why it lives there.
        return options;
    }
}
