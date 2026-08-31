namespace FutureFlags.Domain.Flags.Events;

public sealed record FlagDetailsChangedEvent(
    Guid FlagId,
    string Name,
    string Description,
    DateTimeOffset OccurredAt,
    Guid? CausedBy) : IFlagEvent;
