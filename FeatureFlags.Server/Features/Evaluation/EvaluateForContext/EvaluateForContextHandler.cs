using FeatureFlags.Domain.Shared;
using FeatureFlags.Evaluation;
using FeatureFlags.Server.Evaluation;

namespace FeatureFlags.Server.Features.Evaluation.EvaluateForContext;

/// <summary>
/// Answers key-to-boolean for one context, evaluated here rather than by the caller.
///
/// <para>
/// This is the browser's half of the split. A publishable key is expected to be readable by anyone
/// who can open a bundle, so the definitions never leave the server — the context comes in and
/// booleans go out. A secret key does the opposite through <c>GET /api/evaluation/ruleset</c>,
/// which is faster and offline-tolerant and would be a disclosure here.
/// </para>
/// <para>
/// It costs no more database work than the ruleset route: both read the same cached ruleset, and
/// evaluation is a pass over it in memory.
/// </para>
/// </summary>
public sealed class EvaluateForContextHandler(RulesetProvider provider)
{
    public async Task<Result<EvaluateForContextResponse>> HandleAsync(
        EvaluateForContextQuery query,
        CancellationToken cancellationToken = default)
    {
        var cached = await provider.GetAsync(query.Environment, cancellationToken);

        var evaluated = FlagEvaluator.EvaluateAll(cached.Ruleset, query.Context);

        return Result.Success(new EvaluateForContextResponse(
            cached.Ruleset.Environment,
            cached.ETag,
            // Ordered so that two identical answers render identically, which is what makes a
            // client's own "has anything changed" comparison cheap and honest.
            evaluated.OrderBy(flag => flag.Key, StringComparer.Ordinal)
                .ToDictionary(flag => flag.Key, flag => flag.Value, StringComparer.Ordinal)));
    }
}
