using FeatureFlags.Domain.Flags;
using FeatureFlags.Domain.Segments;
using FeatureFlags.Domain.Shared;

namespace FeatureFlags.Server.Features.Segments.DeleteSegment;

/// <summary>
/// Retires a segment, refusing while anything still points at it.
///
/// <para>
/// The guard lives here rather than on the aggregate because a segment cannot answer who targets it
/// — that is a fact about flags. It is checked across <em>every</em> environment, not just the one
/// the console happens to be showing: a segment still holding up production is not deletable because
/// development no longer needs it.
/// </para>
/// <para>
/// The check races with a flag being targeted a moment later, and deliberately is not locked
/// against. Every evaluation engine already treats a segment key it cannot resolve as a non-match,
/// so the worst outcome of losing that race is a flag reaching nobody — which is the same outcome
/// as the segment matching nobody, and the safe direction.
/// </para>
/// </summary>
public sealed class DeleteSegmentHandler(
    ISegmentRepository repository,
    IFlagViewRepository flags,
    TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(DeleteSegmentCommand command, CancellationToken cancellationToken = default)
    {
        var segmentResult = (await repository.GetByKeyAsync(command.Key, cancellationToken))
            .ToResult(SegmentErrors.NotFound(command.Key));

        if (segmentResult.IsFailure)
            return segmentResult;

        var segment = segmentResult.Value;

        var targeting = await flags.ListTargetingAsync(command.Key, cancellationToken);
        if (targeting.Count > 0)
            return Result.Failure(SegmentErrors.StillTargeted(command.Key, targeting));

        var deleteResult = segment.Delete(timeProvider.GetUtcNow(), command.CausedBy);
        if (deleteResult.IsFailure)
            return deleteResult;

        return await repository.SaveChangesAsync(cancellationToken);
    }
}
