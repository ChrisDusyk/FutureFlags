namespace FeatureFlags.Domain.Segments.Events;

/// <summary>
/// Something that happened to a segment. The sequence number that orders these within a segment's
/// stream is assigned when an event is appended, not carried on the event itself — the same
/// arrangement <see cref="Flags.Events.IFlagEvent"/> has, and for the same reason.
/// </summary>
public interface ISegmentEvent
{
    Guid SegmentId { get; }

    DateTimeOffset OccurredAt { get; }

    /// <summary>Who caused this. Every mutating endpoint requires a signed-in user, so this is only
    /// ever null for events a migration backfilled — of which, for segments, there are none.</summary>
    Guid? CausedBy { get; }
}
