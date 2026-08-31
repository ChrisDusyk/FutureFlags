using FutureFlags.Domain.Shared;

namespace FutureFlags.Domain.Flags;

public interface IFeatureFlagRepository
{
    Task<Option<FeatureFlag>> GetByKeyAsync(FlagKey key, CancellationToken cancellationToken = default);

    Task AddAsync(FeatureFlag flag, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits pending changes. Returns a failure for conflicts the store itself detects — a key
    /// taken between a caller's check and its write. Genuinely unexpected failures still throw.
    /// </summary>
    Task<Result> SaveChangesAsync(CancellationToken cancellationToken = default);
}
