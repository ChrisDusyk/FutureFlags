using FeatureFlags.Domain.Environments;
using FeatureFlags.Domain.Segments;

namespace FeatureFlags.Domain.Flags.Events;

/// <summary>
/// Which segments a flag reaches in one environment, as a whole set rather than a diff — the same
/// reasoning as <see cref="Segments.Events.SegmentDefinitionChangedEvent"/>: a replay that has to be
/// right about every intermediate add and remove is a replay with more ways to be wrong.
/// </summary>
public sealed record FlagTargetingChangedEvent(
    Guid FlagId,
    EnvironmentKey Environment,
    IReadOnlyList<SegmentKey> Segments,
    DateTimeOffset OccurredAt,
    Guid? CausedBy) : IFlagEvent
{
    /// <summary>
    /// Value equality over the set. A record compares <see cref="IReadOnlyList{T}"/> by reference,
    /// which would make two events carrying the same segments unequal — harmless in production,
    /// where nothing compares events, and quietly fatal in a test that thinks it is checking one.
    /// </summary>
    public bool Equals(FlagTargetingChangedEvent? other) =>
        other is not null
        && FlagId == other.FlagId
        && Environment == other.Environment
        && OccurredAt == other.OccurredAt
        && CausedBy == other.CausedBy
        && Segments.SequenceEqual(other.Segments);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(FlagId);
        hash.Add(Environment);
        hash.Add(OccurredAt);
        hash.Add(CausedBy);

        foreach (var segment in Segments)
            hash.Add(segment);

        return hash.ToHashCode();
    }
}
