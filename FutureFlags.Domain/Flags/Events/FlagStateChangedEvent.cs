using FutureFlags.Domain.Environments;
using FutureFlags.Evaluation;

namespace FutureFlags.Domain.Flags.Events;

/// <summary>
/// A flag was turned on or off in one environment.
///
/// <para>
/// <see cref="OnVariant"/> and <see cref="OffVariant"/> name which of the flag's variants is served
/// when it reaches a context and when it does not. Per environment rather than on the flag, because
/// that is the axis everything else about a flag's state already varies on — a future string flag
/// can serve one variant in staging and another in production without being two flags. For a
/// boolean flag they are always <c>on</c> and <c>off</c>.
/// </para>
/// <para>
/// Added to this type rather than carried by a new one, for the reason spelled out on
/// <see cref="FlagCreatedEvent"/>.
/// </para>
/// </summary>
public sealed record FlagStateChangedEvent(
    Guid FlagId,
    EnvironmentKey Environment,
    bool IsEnabled,
    string OnVariant,
    string OffVariant,
    DateTimeOffset OccurredAt,
    Guid? CausedBy) : IFlagEvent
{
    /// <summary>A boolean flag's state, serving the standard variant names.</summary>
    public FlagStateChangedEvent(
        Guid flagId,
        EnvironmentKey environment,
        bool isEnabled,
        DateTimeOffset occurredAt,
        Guid? causedBy)
        : this(flagId, environment, isEnabled, FlagVariantNames.On, FlagVariantNames.Off, occurredAt, causedBy)
    {
    }
}
