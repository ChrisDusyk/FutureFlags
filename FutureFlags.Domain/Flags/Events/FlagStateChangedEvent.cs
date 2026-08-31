using FutureFlags.Domain.Environments;

namespace FutureFlags.Domain.Flags.Events;

public sealed record FlagStateChangedEvent(
    Guid FlagId,
    EnvironmentKey Environment,
    bool IsEnabled,
    DateTimeOffset OccurredAt,
    Guid? CausedBy) : IFlagEvent;
