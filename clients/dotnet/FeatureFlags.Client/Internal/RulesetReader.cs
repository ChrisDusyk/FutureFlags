using System;
using FeatureFlags.Evaluation;

namespace FeatureFlags.Client.Internal;

/// <summary>
/// Answering one flag from a ruleset, shared by the base client and the Redis-backed one.
///
/// <para>
/// Two callers, one behaviour. The alternative — each evaluating its own way — is two clients in
/// one solution that can disagree about the same ruleset, which is exactly the failure the shared
/// evaluation source exists to prevent one layer down.
/// </para>
/// </summary>
internal static class RulesetReader
{
    public static bool IsEnabled(
        Ruleset? ruleset,
        string key,
        FlagContext? context,
        FlagContext? defaults,
        bool defaultValue)
    {
        if (ruleset is null)
        {
            return defaultValue;
        }

        var flag = FindFlag(ruleset, key);

        // An unknown key is the caller's default rather than false. It is the one question the
        // evaluator has no opinion on: it can say whether a flag it has is on, not what a flag it
        // has never heard of ought to mean.
        if (flag is null)
        {
            return defaultValue;
        }

        return FlagEvaluator.Evaluate(
            flag,
            ruleset.SegmentsByKey(),
            (context ?? FlagContext.Empty).WithDefaults(defaults));
    }

    /// <summary>
    /// Case-insensitive, because that is how this package has always compared a flag key, and
    /// changing it now would break <c>IsEnabledAsync("New-Checkout")</c> silently and only at run
    /// time. A linear scan is right here: a ruleset holds tens of flags rather than thousands, and
    /// it is replaced wholesale on every refresh, so any index would be rebuilt just as often as
    /// it was used.
    /// </summary>
    private static RulesetFlag? FindFlag(Ruleset ruleset, string key)
    {
        for (var i = 0; i < ruleset.Flags.Count; i++)
        {
            if (string.Equals(ruleset.Flags[i].Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return ruleset.Flags[i];
            }
        }

        return null;
    }
}
