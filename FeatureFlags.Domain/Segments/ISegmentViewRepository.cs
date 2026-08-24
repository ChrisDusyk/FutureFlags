using FeatureFlags.Domain.Segments.Events;
using FeatureFlags.Domain.Shared;

namespace FeatureFlags.Domain.Segments;

/// <summary>Read-only access to the projected current state of every live segment.</summary>
public interface ISegmentViewRepository
{
    /// <summary>Every segment that has not been retired, ordered by key.</summary>
    Task<IReadOnlyList<SegmentView>> ListAsync(CancellationToken cancellationToken = default);

    Task<Option<SegmentView>> GetByKeyAsync(SegmentKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Which of these keys belong to a live segment. One round trip rather than one per key,
    /// because the caller that needs this — setting a flag's targeting — always has a set.
    /// </summary>
    Task<IReadOnlyList<SegmentKey>> FilterExistingAsync(
        IReadOnlyCollection<SegmentKey> keys,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One segment's full event history, newest first. Small and unpaginated, on the same reasoning
    /// as a flag's. Takes the id rather than the key because every caller already has one from a
    /// prior <see cref="GetByKeyAsync"/>.
    /// </summary>
    Task<IReadOnlyList<ISegmentEvent>> GetHistoryAsync(Guid segmentId, CancellationToken cancellationToken = default);
}
