using FeatureFlags.Domain.Shared;
using FeatureFlags.Evaluation;
using FeatureFlags.Server.Evaluation;

namespace FeatureFlags.Server.Features.Flags.EvaluateFlags;

/// <summary>
/// Every flag's state in one environment, evaluated for nobody in particular.
///
/// <para>
/// This route predates segments and keeps its exact shape, because it is half of the compatibility
/// surface <c>clients/README.md</c> documents. What changed underneath is that it now evaluates
/// against <see cref="FlagContext.Empty"/> rather than reading a bare boolean — so a flag
/// that has been given targeting reads <c>false</c> here.
/// </para>
/// <para>
/// That is a real behaviour change and it is the safe direction. A client that has never been told
/// who is asking cannot be told a feature is on for them; the alternative — reporting a targeted
/// flag as on to everyone who has not upgraded — would hand a feature to exactly the people it was
/// narrowed away from. The console warns when targeting is first added, because on the next poll
/// every SDK still using this route will see the flag go dark.
/// </para>
/// <para>
/// Caching moved to <see cref="RulesetProvider"/> when a second and third route needed the same
/// data. The bargain is unchanged: five seconds of staleness after a write, in exchange for a poll
/// that mostly does not reach Postgres.
/// </para>
/// </summary>
public sealed class EvaluateFlagsHandler(RulesetProvider provider)
{
    public async Task<Result<EvaluatedFlags>> HandleAsync(
        EvaluateFlagsQuery query,
        CancellationToken cancellationToken = default)
    {
        var cached = await provider.GetAsync(query.Environment, cancellationToken);

        return Result.Success(Evaluate(cached));
    }

    /// <summary>
    /// Flattens a ruleset to the key-and-boolean shape this route has always answered with.
    ///
    /// <para>
    /// The tag is the ruleset's own rather than a second one computed here. Two flags that evaluate
    /// the same way for nobody can still differ in who they would reach — so a tag over the booleans
    /// alone would tell a caller nothing had changed when the targeting underneath had, and the
    /// answer would be wrong for every client that later asked with a context.
    /// </para>
    /// </summary>
    internal static EvaluatedFlags Evaluate(CachedRuleset cached)
    {
        var evaluated = new Dictionary<string, bool>(cached.Ruleset.Flags.Count, StringComparer.Ordinal);

        foreach (var flag in cached.Ruleset.Flags)
            evaluated[flag.Key] = FlagEvaluator.Evaluate(flag, cached.Ruleset.SegmentsByKey(), FlagContext.Empty);

        return new EvaluatedFlags(
            new EvaluateFlagsResponse(cached.Ruleset.Environment, evaluated),
            cached.ETag);
    }
}

/// <summary>The response and the tag that identifies this exact version of it.</summary>
public sealed record EvaluatedFlags(EvaluateFlagsResponse Response, string ETag);
