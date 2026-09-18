using FutureFlags.Domain.Shared;
using FutureFlags.Evaluation;
using FutureFlags.Server.Evaluation;

namespace FutureFlags.Server.Features.Evaluation.OfrepEvaluateFlag;

/// <summary>
/// One flag's resolution for one context — OFREP's single-flag evaluation.
///
/// <para>
/// The one place this platform's "a key nobody has heard of is simply off" rule does not hold. OFREP
/// specifies a 404 carrying <c>FLAG_NOT_FOUND</c>, and that is the better answer here: a provider
/// reads the code and returns the caller's own default, which is what an application asking for a
/// flag it believes exists actually wants. Answering <c>false</c> would look like a deliberate off.
/// </para>
/// </summary>
public sealed class OfrepEvaluateFlagHandler(RulesetProvider provider)
{
    public async Task<Result<OfrepFlagResult>> HandleAsync(
        OfrepEvaluateFlagQuery query,
        CancellationToken cancellationToken = default)
    {
        var cached = await provider.GetAsync(query.Environment, cancellationToken);

        // Ordinal-ignore-case, matching how both SDKs have always looked a flag key up. A FlagKey
        // is already lowercased by its own factory, so this only forgives a caller that was not.
        var flag = cached.Ruleset.Flags.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, query.Key.Value, StringComparison.OrdinalIgnoreCase));

        if (flag is null)
        {
            return Result.Failure<OfrepFlagResult>(OfrepErrors.FlagNotFound(query.Key.Value));
        }

        var resolution = FlagEvaluator.Resolve(flag, cached.Ruleset.SegmentsByKey(), query.Context);

        return Result.Success(new OfrepFlagResult(
            flag.Key,
            resolution.Value,
            resolution.Variant,
            resolution.Reason,
            resolution.FlagMetadata));
    }
}
