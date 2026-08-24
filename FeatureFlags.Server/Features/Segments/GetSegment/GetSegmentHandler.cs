using FeatureFlags.Domain.Flags;
using FeatureFlags.Domain.Segments;
using FeatureFlags.Domain.Shared;

namespace FeatureFlags.Server.Features.Segments.GetSegment;

/// <summary>
/// One segment, with everywhere it is being used.
///
/// <para>
/// Two repositories because the answer spans two aggregates: a segment does not know who points at
/// it, and targeting is a fact about flags. The precedent is <c>GetFlagHistoryHandler</c> reaching
/// for <c>IUserRepository</c> to put names on events.
/// </para>
/// </summary>
public sealed class GetSegmentHandler(ISegmentViewRepository segments, IFlagViewRepository flags)
{
    public async Task<Result<GetSegmentResponse>> HandleAsync(
        GetSegmentQuery query,
        CancellationToken cancellationToken = default)
    {
        var segmentResult = (await segments.GetByKeyAsync(query.Key, cancellationToken))
            .ToResult(SegmentErrors.NotFound(query.Key));

        if (segmentResult.IsFailure)
            return Result.Failure<GetSegmentResponse>(segmentResult.Error);

        var targeting = await flags.ListTargetingAsync(query.Key, cancellationToken);

        return Result.Success(GetSegmentResponse.From(segmentResult.Value, targeting));
    }
}
