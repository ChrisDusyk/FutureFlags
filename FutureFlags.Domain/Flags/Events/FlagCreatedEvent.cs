using FutureFlags.Evaluation;

namespace FutureFlags.Domain.Flags.Events;

/// <summary>
/// A flag came into existence.
///
/// <para>
/// <see cref="ValueType"/> and <see cref="Variants"/> were added rather than being carried by a new
/// event type, and that is the point: <see cref="FeatureFlag"/>'s <c>Apply</c> throws on an event
/// type it does not recognize, so introducing one makes a deploy one-way — roll the server back and
/// every read of an affected flag throws. Extra fields on an existing type have no such effect. The
/// persisted payload spells them optional and defaults them to the boolean shape, so a stream
/// written before this shipped replays unchanged and a stream written after it replays on an older
/// build.
/// </para>
/// </summary>
public sealed record FlagCreatedEvent(
    Guid FlagId,
    FlagKey Key,
    string Name,
    string Description,
    FlagValueType ValueType,
    FlagVariants Variants,
    DateTimeOffset OccurredAt,
    Guid? CausedBy) : IFlagEvent
{
    /// <summary>A boolean flag with the standard variant pair — every flag this build creates.</summary>
    public FlagCreatedEvent(
        Guid flagId,
        FlagKey key,
        string name,
        string description,
        DateTimeOffset occurredAt,
        Guid? causedBy)
        : this(flagId, key, name, description, FlagValueType.Boolean, FlagVariants.BooleanPair, occurredAt, causedBy)
    {
    }
}
