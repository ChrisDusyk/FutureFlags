namespace FeatureFlags.Domain.Segments.Events;

public sealed record SegmentCreatedEvent(
    Guid SegmentId,
    SegmentKey Key,
    string Name,
    string Description,
    DateTimeOffset OccurredAt,
    Guid? CausedBy) : ISegmentEvent;
