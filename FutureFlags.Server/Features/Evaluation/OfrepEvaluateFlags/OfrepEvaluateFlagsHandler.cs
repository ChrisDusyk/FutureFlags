using FutureFlags.Domain.Shared;
using FutureFlags.Evaluation;
using FutureFlags.Server.Evaluation;

namespace FutureFlags.Server.Features.Evaluation.OfrepEvaluateFlags;

/// <summary>
/// Every flag's resolution for one context — OFREP's bulk evaluation.
///
/// <para>
/// Reads the same cached ruleset as every other evaluation route, so an OFREP client and a native
/// one asking the same question at the same moment cannot be given different answers.
/// </para>
/// </summary>
public sealed class OfrepEvaluateFlagsHandler(RulesetProvider provider)
{
    public async Task<Result<OfrepEvaluatedFlags>> HandleAsync(
        OfrepEvaluateFlagsQuery query,
        CancellationToken cancellationToken = default)
    {
        var cached = await provider.GetAsync(query.Environment, cancellationToken);

        var resolved = FlagEvaluator.ResolveAll(cached.Ruleset, query.Context);

        var flags = resolved
            // Ordered so two identical answers render identically, which is what makes a client's
            // own "has anything changed" comparison cheap and honest.
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new OfrepFlagResult(
                entry.Key,
                entry.Value.Value,
                entry.Value.Variant,
                entry.Value.Reason,
                entry.Value.FlagMetadata))
            .ToList();

        var response = new OfrepEvaluateFlagsResponse(flags, NoMetadata);

        return Result.Success(new OfrepEvaluatedFlags(
            response,
            Ofrep.ETagFor(cached.ETag, query.Context)));
    }

    private static readonly IReadOnlyDictionary<string, AttributeValue> NoMetadata =
        new Dictionary<string, AttributeValue>(StringComparer.Ordinal);
}
