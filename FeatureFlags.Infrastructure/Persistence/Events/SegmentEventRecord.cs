namespace FeatureFlags.Infrastructure.Persistence.Events;

/// <summary>
/// The persisted shape of one <see cref="FeatureFlags.Domain.Segments.Events.ISegmentEvent"/> — a
/// row in <c>segment_events</c>. Infrastructure-only, on the same terms as
/// <see cref="FlagEventRecord"/>: the domain event types stay free of any storage concern.
/// </summary>
internal sealed class SegmentEventRecord
{
    public Guid SegmentId { get; set; }

    /// <summary>This segment's event stream position — 1, 2, 3, … — assigned when the event is
    /// appended. The primary key together with <see cref="SegmentId"/>, which is also what turns a
    /// concurrent writer into a Postgres unique-violation rather than a silent overwrite.</summary>
    public int SequenceNumber { get; set; }

    /// <summary>The discriminator <see cref="SegmentEventSerializer"/> uses to pick which payload
    /// shape to deserialize.</summary>
    public string EventType { get; set; } = null!;

    /// <summary>Event-specific fields only — <see cref="SegmentId"/> and <see cref="OccurredAt"/>
    /// are already columns, so they are not duplicated inside the payload.</summary>
    public string Payload { get; set; } = null!;

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Who caused this. Unlike a flag's, this is never null in practice — segments arrived
    /// after the CausedBy column did, so there is no backfilled history to account for.</summary>
    public Guid? CausedBy { get; set; }
}
