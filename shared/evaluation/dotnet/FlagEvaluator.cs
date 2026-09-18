using System;
using System.Collections.Generic;

namespace FutureFlags.Evaluation;

/// <summary>
/// Whether a flag is on, for one context, and why.
///
/// <para>
/// The whole rule, and there is not more to it in this build:
/// </para>
/// <list type="bullet">
///   <item><description>off in this environment — <c>false</c>, whatever the context says;</description></item>
///   <item><description>on and targeting nobody — <c>true</c> for everyone, which is what a flag
///   meant before segments existed;</description></item>
///   <item><description>on and targeting segments — <c>true</c> only if the context is in at least
///   one of them.</description></item>
/// </list>
/// <para>
/// Ordered rules with first-match-wins and percentage rollout are a later feature, and the shape
/// above is deliberately the subset of them that needs no ordering to be unambiguous.
/// </para>
/// <para>
/// <see cref="Resolve"/> is the real entry point and <see cref="Evaluate"/> reads its answer as a
/// boolean. The reasons it attaches are OpenFeature's, and which one goes with which situation is
/// pinned by <c>shared/evaluation/conformance/flags.json</c> rather than decided independently by
/// each of the three implementations — see the table in that file's notes. The one that most
/// repays reading twice: a flag that is on, targets segments, and matched none of them resolves
/// <see cref="EvaluationReason.Default"/> with <em>no</em> error code. It is a normal answer. The
/// subject is simply not in the segment, and reporting that as an error would make every
/// deliberately narrowed flag look like an outage.
/// </para>
/// </summary>
public static class FlagEvaluator
{
    /// <summary>Whether one flag is on for one context. A null flag — a key this ruleset does not
    /// carry — is off, and a caller wanting a different answer supplies its own default.</summary>
    public static bool Evaluate(
        RulesetFlag? flag,
        IReadOnlyDictionary<string, RulesetSegment>? segments,
        FlagContext? context) =>
        Resolve(flag, segments, context).AsBoolean();

    /// <summary>
    /// One flag's answer for one context, with the variant and reason attached.
    ///
    /// <para>
    /// A key this ruleset does not carry resolves to <see cref="EvaluationReason.Error"/> with
    /// <see cref="EvaluationErrorCode.FlagNotFound"/> — the one abnormal case here. The value it
    /// carries is <c>false</c>, which is what <see cref="Evaluate"/> has always answered, but a
    /// caller holding the resolution can tell "off" from "never heard of it" and an OpenFeature
    /// provider can return the caller's own default instead.
    /// </para>
    /// </summary>
    public static FlagResolution Resolve(
        RulesetFlag? flag,
        IReadOnlyDictionary<string, RulesetSegment>? segments,
        FlagContext? context)
    {
        // A flag nobody has heard of is not a flag that is on. The callers that want a different
        // answer supply their own default rather than asking this to guess.
        if (flag is null)
            return NotFound;

        if (!flag.IsEnabled)
            return Off(flag, EvaluationReason.Disabled);

        if (flag.TargetedSegments.Count == 0)
            return On(flag, EvaluationReason.Static);

        context ??= FlagContext.Empty;

        for (var i = 0; i < flag.TargetedSegments.Count; i++)
        {
            var key = flag.TargetedSegments[i];

            // A targeted segment this ruleset does not carry is a non-match rather than a failure.
            // It happens legitimately — a segment deleted between the write that targeted it and
            // this read — and a flag that starts throwing because somebody tidied up a segment
            // would be a far worse outcome than one that quietly reaches nobody.
            if (key is null || segments is null || !segments.TryGetValue(key, out var segment))
                continue;

            if (SegmentMatcher.Matches(segment, context))
                return On(flag, EvaluationReason.TargetingMatch);
        }

        return Off(flag, EvaluationReason.Default);
    }

    /// <summary>Every flag in the ruleset, answered for one context.</summary>
    public static IReadOnlyDictionary<string, bool> EvaluateAll(Ruleset? ruleset, FlagContext? context)
    {
        var resolved = ResolveAll(ruleset, context);
        var evaluated = new Dictionary<string, bool>(resolved.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in resolved)
            evaluated[entry.Key] = entry.Value.AsBoolean();

        return evaluated;
    }

    /// <summary>Every flag in the ruleset, resolved for one context.</summary>
    public static IReadOnlyDictionary<string, FlagResolution> ResolveAll(Ruleset? ruleset, FlagContext? context)
    {
        if (ruleset is null)
            return new Dictionary<string, FlagResolution>(StringComparer.OrdinalIgnoreCase);

        // Ordinal-ignore-case because that is how both SDKs have always looked a flag key up, and
        // dropping that now would silently break `IsEnabled("New-Checkout")` for anyone relying on it.
        var resolved = new Dictionary<string, FlagResolution>(ruleset.Flags.Count, StringComparer.OrdinalIgnoreCase);
        var segments = ruleset.SegmentsByKey();
        var against = context ?? FlagContext.Empty;

        for (var i = 0; i < ruleset.Flags.Count; i++)
        {
            var flag = ruleset.Flags[i];

            if (flag?.Key is not null)
                resolved[flag.Key] = Resolve(flag, segments, against);
        }

        return resolved;
    }

    private static readonly FlagResolution NotFound = new(
        FlagValue.False,
        variant: null,
        EvaluationReason.Error,
        EvaluationErrorCode.FlagNotFound,
        "No flag by that key exists in this environment.");

    private static FlagResolution On(RulesetFlag flag, string reason) =>
        new(flag.OnValue, flag.OnVariant, reason);

    private static FlagResolution Off(RulesetFlag flag, string reason) =>
        new(flag.OffValue, flag.OffVariant, reason);
}
