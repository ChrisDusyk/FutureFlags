using System;
using FutureFlags.Evaluation;

namespace FutureFlags.Client.Internal;

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
    /// <summary>
    /// Whether a flag is on, falling back to <paramref name="defaultValue"/> for anything the
    /// ruleset cannot answer. A reading of <see cref="Resolve"/>, so the boolean surface and the
    /// resolution surface cannot drift.
    /// </summary>
    public static bool IsEnabled(
        Ruleset? ruleset,
        string key,
        FlagContext? context,
        FlagContext? defaults,
        bool defaultValue) =>
        Resolve(ruleset, key, context, defaults).AsBoolean(defaultValue);

    /// <summary>
    /// One flag's resolution, with the variant and reason attached.
    ///
    /// <para>
    /// Two failures are told apart here rather than collapsed into "off". A ruleset that has never
    /// loaded is <c>PROVIDER_NOT_READY</c>, and a key this installation does not carry is
    /// <c>FLAG_NOT_FOUND</c> — the one question the evaluator has no opinion on, since it can say
    /// whether a flag it has is on but not what a flag it has never heard of ought to mean. Both
    /// still read as the caller's default through <see cref="FlagResolution.AsBoolean"/>, so the
    /// answers this package has always given are unchanged; what is new is that an OpenFeature
    /// provider can now say <em>why</em>.
    /// </para>
    /// </summary>
    public static FlagResolution Resolve(
        Ruleset? ruleset,
        string key,
        FlagContext? context,
        FlagContext? defaults)
    {
        if (ruleset is null)
        {
            return new FlagResolution(
                FlagValue.False,
                variant: null,
                EvaluationReason.Error,
                EvaluationErrorCode.ProviderNotReady,
                "No ruleset has been loaded yet.");
        }

        return FlagEvaluator.Resolve(
            FindFlag(ruleset, key),
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
