using FeatureFlags.Domain.Shared;
using FeatureFlags.Server.Evaluation;

namespace FeatureFlags.Server.Features.Evaluation.GetRuleset;

/// <summary>
/// Hands back the cached ruleset unchanged. There is nothing to evaluate here — that is the point
/// of this route: a secret-key client pulls the definitions and decides for itself, so a flag read
/// on a hot path is a map lookup rather than a request.
/// </summary>
public sealed class GetRulesetHandler(RulesetProvider provider)
{
    public async Task<Result<CachedRuleset>> HandleAsync(
        GetRulesetQuery query,
        CancellationToken cancellationToken = default) =>
        Result.Success(await provider.GetAsync(query.Environment, cancellationToken));
}
