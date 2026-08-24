namespace FeatureFlags.Domain.Segments.Events;

public sealed record SegmentDetailsChangedEvent(
    Guid SegmentId,
    string Name,
    string Description,
    DateTimeOffset OccurredAt,
    Guid? CausedBy) : ISegmentEvent;
