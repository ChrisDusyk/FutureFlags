using FutureFlags.Domain.Shared;

namespace FutureFlags.Domain.SdkKeys;

public interface ISdkKeyRepository
{
    /// <summary>Every key, revoked ones included, newest first. The console shows both.</summary>
    Task<IReadOnlyList<SdkKey>> ListAsync(CancellationToken cancellationToken = default);

    Task<Option<SdkKey>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The lookup on the authenticated read path, so it has to be the indexed one. Returns revoked
    /// keys too — telling a revoked key from an unknown one is the caller's job, not the store's.
    /// </summary>
    Task<Option<SdkKey>> GetBySelectorAsync(string selector, CancellationToken cancellationToken = default);

    Task AddAsync(SdkKey key, CancellationToken cancellationToken = default);

    Task<Result> SaveChangesAsync(CancellationToken cancellationToken = default);
}
