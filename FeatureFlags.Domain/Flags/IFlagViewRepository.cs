using FeatureFlags.Domain.Flags.Events;
using FeatureFlags.Domain.Segments;
using FeatureFlags.Domain.Shared;

namespace FeatureFlags.Domain.Flags;

/// <summary>Read-only access to the projected current state of every flag. See <see cref="FlagView"/>.</summary>
public interface IFlagViewRepository
{
    /// <summary>Every flag, ordered by key. Each carries its state for all environments.</summary>
    Task<IReadOnlyList<FlagView>> ListAsync(CancellationToken cancellationToken = default);

    Task<Option<FlagView>> GetByKeyAsync(FlagKey key, CancellationToken cancellationToken = default);

    /// <summary>One flag's full event history, newest first. Small and unpaginated — a flag's
    /// event count stays in the dozens even over a long life. Takes the flag's id rather than its
    /// key because every caller already has one from a prior <see cref="GetByKeyAsync"/> — that is
    /// also how a caller answers "does this flag exist" before calling this.</summary>
    Task<IReadOnlyList<IFlagEvent>> GetHistoryAsync(Guid flagId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Everywhere this segment is targeted — one entry per flag and environment, ordered by key
    /// then by environment. Answers both "what am I about to break" on the segment screen and
    /// "may this be deleted", which are the same question asked at different volumes.
    /// </summary>
    Task<IReadOnlyList<FlagTargetingView>> ListTargetingAsync(
        SegmentKey segment,
        CancellationToken cancellationToken = default);
}
