namespace FeatureFlags.Domain.Segments.Events;

/// <summary>
/// A segment was retired. Its row is tombstoned rather than removed and its key is never reissued —
/// see <see cref="Segment.Delete"/>.
/// </summary>
public sealed record SegmentDeletedEvent(
    Guid SegmentId,
    DateTimeOffset OccurredAt,
    Guid? CausedBy) : ISegmentEvent;
