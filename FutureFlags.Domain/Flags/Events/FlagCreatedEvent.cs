namespace FutureFlags.Domain.Flags.Events;

public sealed record FlagCreatedEvent(
    Guid FlagId,
    FlagKey Key,
    string Name,
    string Description,
    DateTimeOffset OccurredAt,
    Guid? CausedBy) : IFlagEvent;
