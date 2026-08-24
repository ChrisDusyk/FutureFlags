using FeatureFlags.Domain.Segments;
using FeatureFlags.Domain.Segments.Events;
using FeatureFlags.Domain.Shared;
using FeatureFlags.Domain.Users;

namespace FeatureFlags.Server.Features.Segments.GetSegmentHistory;

public sealed class GetSegmentHistoryHandler(
    ISegmentRepository segments,
    ISegmentViewRepository viewRepository,
    IUserRepository userRepository)
{
    public async Task<Result<GetSegmentHistoryResponse>> HandleAsync(
        GetSegmentHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        // Resolved through the write side, tombstones included, rather than the (filtered) view —
        // a retired segment's events are exactly what tombstoning instead of deleting was for.
        var segmentResult = (await segments.GetByKeyAsync(query.Key, cancellationToken))
            .ToResult(SegmentErrors.NotFound(query.Key));

        if (segmentResult.IsFailure)
            return Result.Failure<GetSegmentHistoryResponse>(segmentResult.Error);

        var events = await viewRepository.GetHistoryAsync(segmentResult.Value.Id, cancellationToken);

        // One round trip for every distinct actor rather than one per event, matching
        // GetFlagHistoryHandler.
        var causedByIds = events
            .Select(@event => @event.CausedBy)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var users = await userRepository.GetByIdsAsync(causedByIds, cancellationToken);
        var names = users.ToDictionary(user => user.Id, user => user.Name is { Length: > 0 } ? user.Name : user.Email);

        return Result.Success(new GetSegmentHistoryResponse([.. events.Select(@event => ToEntry(@event, names))]));
    }

    private static SegmentHistoryEntryResponse ToEntry(ISegmentEvent @event, IReadOnlyDictionary<Guid, string> names)
    {
        var causedByName = @event.CausedBy is { } id && names.TryGetValue(id, out var name) ? name : null;

        return @event switch
        {
            SegmentCreatedEvent created => new SegmentHistoryEntryResponse(
                "SegmentCreated", created.OccurredAt, causedByName, created.Name, created.Description, null),

            SegmentDetailsChangedEvent detailsChanged => new SegmentHistoryEntryResponse(
                "SegmentDetailsChanged", detailsChanged.OccurredAt, causedByName,
                detailsChanged.Name, detailsChanged.Description, null),

            SegmentDefinitionChangedEvent definitionChanged => new SegmentHistoryEntryResponse(
                "SegmentDefinitionChanged", definitionChanged.OccurredAt, causedByName, null, null,
                SegmentHistoryDefinitionResponse.From(definitionChanged.Definition)),

            SegmentDeletedEvent deleted => new SegmentHistoryEntryResponse(
                "SegmentDeleted", deleted.OccurredAt, causedByName, null, null, null),

            _ => throw new InvalidOperationException($"Unrecognized segment event type '{@event.GetType()}'."),
        };
    }
}
