using FeatureFlags.Domain.Shared;

namespace FeatureFlags.Domain.Segments;

/// <summary>The write side. See <see cref="Flags.IFeatureFlagRepository"/> — the same shape, and
/// the same identity-map obligation within a scope.</summary>
public interface ISegmentRepository
{
    /// <summary>
    /// The segment with this key, <em>including a retired one</em>. Deliberately not filtered:
    /// creating a segment over a retired key has to be refused with a different answer than
    /// creating one over a live key, and this is where the caller learns which it is.
    /// </summary>
    Task<Option<Segment>> GetByKeyAsync(SegmentKey key, CancellationToken cancellationToken = default);

    Task AddAsync(Segment segment, CancellationToken cancellationToken = default);

    Task<Result> SaveChangesAsync(CancellationToken cancellationToken = default);
}
