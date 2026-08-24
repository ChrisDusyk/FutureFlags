using System;
using System.Collections.Generic;

namespace FeatureFlags.Evaluation;

/// <summary>
/// Whether a flag is on, for one context.
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
/// </summary>
public static class FlagEvaluator
{
    /// <summary>Whether one flag is on for one context. A null flag — a key this ruleset does not
    /// carry — is off, and a caller wanting a different answer supplies its own default.</summary>
    public static bool Evaluate(
        RulesetFlag? flag,
        IReadOnlyDictionary<string, RulesetSegment>? segments,
        FlagContext? context)
    {
        // A flag nobody has heard of is not a flag that is on. The callers that want a different
        // answer supply their own default rather than asking this to guess.
        if (flag is null)
            return false;

        if (!flag.IsEnabled)
            return false;

        if (flag.TargetedSegments.Count == 0)
            return true;

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
                return true;
        }

        return false;
    }

    /// <summary>Every flag in the ruleset, answered for one context.</summary>
    public static IReadOnlyDictionary<string, bool> EvaluateAll(Ruleset? ruleset, FlagContext? context)
    {
        if (ruleset is null)
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // Ordinal-ignore-case because that is how both SDKs have always looked a flag key up, and
        // dropping that now would silently break `IsEnabled("New-Checkout")` for anyone relying on it.
        var evaluated = new Dictionary<string, bool>(ruleset.Flags.Count, StringComparer.OrdinalIgnoreCase);
        var segments = ruleset.SegmentsByKey();
        var resolved = context ?? FlagContext.Empty;

        for (var i = 0; i < ruleset.Flags.Count; i++)
        {
            var flag = ruleset.Flags[i];

            if (flag?.Key is not null)
                evaluated[flag.Key] = Evaluate(flag, segments, resolved);
        }

        return evaluated;
    }
}
