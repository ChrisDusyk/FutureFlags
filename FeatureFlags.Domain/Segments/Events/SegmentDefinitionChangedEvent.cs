namespace FeatureFlags.Domain.Segments.Events;

/// <summary>
/// The whole definition, not a diff. A definition is small, it is edited as one thing in the
/// console, and a stream of diffs would mean a replay had to be correct about every one of them to
/// arrive at the right answer.
/// </summary>
public sealed record SegmentDefinitionChangedEvent(
    Guid SegmentId,
    SegmentDefinition Definition,
    DateTimeOffset OccurredAt,
    Guid? CausedBy) : ISegmentEvent;
