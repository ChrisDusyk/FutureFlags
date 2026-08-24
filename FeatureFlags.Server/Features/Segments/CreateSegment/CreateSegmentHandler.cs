using FeatureFlags.Domain.Segments;
using FeatureFlags.Domain.Shared;

namespace FeatureFlags.Server.Features.Segments.CreateSegment;

public sealed class CreateSegmentHandler(ISegmentRepository repository, TimeProvider timeProvider)
{
    public async Task<Result<CreateSegmentResponse>> HandleAsync(
        CreateSegmentCommand command,
        CancellationToken cancellationToken = default)
    {
        var segmentResult = Segment.Create(
            command.Key, command.Name, command.Description, command.Definition,
            timeProvider.GetUtcNow(), command.CausedBy);

        if (segmentResult.IsFailure)
            return Result.Failure<CreateSegmentResponse>(segmentResult.Error);

        var segment = segmentResult.Value;

        // Checked here so the answer can say *which* kind of taken this key is: a live segment is a
        // duplicate, a retired one is a key that will never be reissued. The unique index settles
        // the race either way, and translates to the duplicate answer.
        var existing = await repository.GetByKeyAsync(segment.Key, cancellationToken);
        if (existing.IsSome)
        {
            return Result.Failure<CreateSegmentResponse>(existing.Match(
                found => found.IsDeleted ? SegmentErrors.KeyRetired(segment.Key) : SegmentErrors.DuplicateKey(segment.Key),
                () => SegmentErrors.DuplicateKey(segment.Key)));
        }

        await repository.AddAsync(segment, cancellationToken);

        var saveResult = await repository.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return Result.Failure<CreateSegmentResponse>(saveResult.Error);

        return Result.Success(CreateSegmentResponse.From(segment));
    }
}
