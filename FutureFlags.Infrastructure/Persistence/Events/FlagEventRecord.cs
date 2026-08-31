namespace FutureFlags.Infrastructure.Persistence.Events;

/// <summary>
/// The persisted shape of one <see cref="FutureFlags.Domain.Flags.Events.IFlagEvent"/> — a row in
/// <c>flag_events</c>. Infrastructure-only: the domain event types stay free of any storage concern,
/// and <see cref="FlagEventSerializer"/> is what converts between the two.
/// </summary>
internal sealed class FlagEventRecord
{
    public Guid FlagId { get; set; }

    /// <summary>This flag's event stream position — 1, 2, 3, … — assigned when the event is
    /// appended. The primary key together with <see cref="FlagId"/>, which is also what turns a
    /// concurrent writer into a Postgres unique-violation rather than a silent overwrite.</summary>
    public int SequenceNumber { get; set; }

    /// <summary>"FlagCreated", "FlagStateChanged", or "FlagDetailsChanged" — the discriminator
    /// <see cref="FlagEventSerializer"/> uses to pick which payload shape to deserialize.</summary>
    public string EventType { get; set; } = null!;

    /// <summary>Event-specific fields only — <see cref="FlagId"/> and <see cref="OccurredAt"/> are
    /// already columns, so they are not duplicated inside the payload.</summary>
    public string Payload { get; set; } = null!;

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Who caused this. Null for rows the AddFlagEvents migration's backfill wrote —
    /// there is no real actor to record for history that predates this column.</summary>
    public Guid? CausedBy { get; set; }
}
