using FeatureFlags.Domain.Segments;
using FeatureFlags.Domain.Shared;

namespace FeatureFlags.Server.Features.Segments.UpdateSegment;

public sealed class UpdateSegmentHandler(ISegmentRepository repository, TimeProvider timeProvider)
{
    public async Task<Result<UpdateSegmentResponse>> HandleAsync(
        UpdateSegmentCommand command,
        CancellationToken cancellationToken = default)
    {
        var segmentResult = (await repository.GetByKeyAsync(command.Key, cancellationToken))
            .ToResult(SegmentErrors.NotFound(command.Key));

        if (segmentResult.IsFailure)
            return Result.Failure<UpdateSegmentResponse>(segmentResult.Error);

        var segment = segmentResult.Value;

        // A retired segment reads as not-found from the read side but is still reachable here, so
        // say the more useful thing rather than pretending it never existed.
        if (segment.IsDeleted)
            return Result.Failure<UpdateSegmentResponse>(SegmentErrors.Deleted(command.Key));

        var now = timeProvider.GetUtcNow();

        var detailsResult = segment.UpdateDetails(command.Name, command.Description, now, command.CausedBy);
        if (detailsResult.IsFailure)
            return Result.Failure<UpdateSegmentResponse>(detailsResult.Error);

        var definitionResult = segment.ChangeDefinition(command.Definition, now, command.CausedBy);
        if (definitionResult.IsFailure)
            return Result.Failure<UpdateSegmentResponse>(definitionResult.Error);

        // Both mutators are idempotent, so an unchanged save raises no events and appends nothing —
        // there is no "did anything change?" branch to keep in step with them here.
        var saveResult = await repository.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return Result.Failure<UpdateSegmentResponse>(saveResult.Error);

        return Result.Success(UpdateSegmentResponse.From(segment));
    }
}
