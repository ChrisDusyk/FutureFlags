using FeatureFlags.Domain.Flags;
using FeatureFlags.Domain.Segments;
using FeatureFlags.Domain.Shared;

namespace FeatureFlags.Server.Features.Flags.SetFlagTargeting;

public sealed class SetFlagTargetingHandler(
    IFeatureFlagRepository repository,
    ISegmentViewRepository segments,
    TimeProvider timeProvider)
{
    public async Task<Result<SetFlagTargetingResponse>> HandleAsync(
        SetFlagTargetingCommand command,
        CancellationToken cancellationToken = default)
    {
        var flagResult = (await repository.GetByKeyAsync(command.Key, cancellationToken))
            .ToResult(FlagErrors.NotFound(command.Key));

        if (flagResult.IsFailure)
            return Result.Failure<SetFlagTargetingResponse>(flagResult.Error);

        // The aggregate holds no repository and so cannot check this itself. It is checked here
        // rather than left to evaluation because pointing a flag at a segment that does not exist
        // is a typo somebody can still fix, and the evaluator's "unknown means no match" would turn
        // it into a flag that silently reaches nobody.
        var missing = await MissingSegmentsAsync(command.Segments, cancellationToken);
        if (missing.Count > 0)
            return Result.Failure<SetFlagTargetingResponse>(SegmentErrors.UnknownSegments(missing));

        var flag = flagResult.Value;

        var targetingResult = flag.SetTargeting(
            command.Environment, command.Segments, timeProvider.GetUtcNow(), command.CausedBy);

        if (targetingResult.IsFailure)
            return Result.Failure<SetFlagTargetingResponse>(targetingResult.Error);

        var saveResult = await repository.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return Result.Failure<SetFlagTargetingResponse>(saveResult.Error);

        return Result.Success(SetFlagTargetingResponse.From(flag, command.Environment));
    }

    private async Task<IReadOnlyList<SegmentKey>> MissingSegmentsAsync(
        IReadOnlyList<SegmentKey> requested,
        CancellationToken cancellationToken)
    {
        if (requested.Count == 0)
            return [];

        var existing = await segments.FilterExistingAsync(requested, cancellationToken);
        var found = existing.ToHashSet();

        return [.. requested.Where(key => !found.Contains(key)).Distinct()];
    }
}
