namespace FutureFlags.Domain.Flags.Events;

/// <summary>
/// Something that happened to a flag. The sequence number that orders these within a flag's
/// stream is assigned when an event is appended, not carried on the event itself.
/// </summary>
public interface IFlagEvent
{
    Guid FlagId { get; }
    DateTimeOffset OccurredAt { get; }

    /// <summary>
    /// Who caused this. Null only for events a migration backfilled from state that predated
    /// this field — every event raised going forward always carries one, since every mutating
    /// endpoint requires a signed-in user.
    /// </summary>
    Guid? CausedBy { get; }
}
