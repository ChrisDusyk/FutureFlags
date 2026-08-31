using FutureFlags.Domain.SdkKeys;
using FutureFlags.Domain.Shared;

namespace FutureFlags.Server.Tests.Fakes;

/// <summary>
/// In-memory stand-in for the EF repository, alongside <see cref="FakeFeatureFlagRepository"/> and
/// for the same reason: it stands in for a Domain interface that several slices depend on.
/// </summary>
internal sealed class FakeSdkKeyRepository : ISdkKeyRepository
{
    private readonly List<SdkKey> _committed = [];
    private readonly List<SdkKey> _pending = [];

    public int SaveChangesCallCount { get; private set; }

    public IReadOnlyList<SdkKey> Committed => _committed;

    public void Seed(SdkKey key) => _committed.Add(key);

    public Task<IReadOnlyList<SdkKey>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SdkKey>>([.. _committed.OrderByDescending(key => key.Id)]);

    public Task<Option<SdkKey>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_committed.FirstOrDefault(key => key.Id == id).ToOption());

    public Task<Option<SdkKey>> GetBySelectorAsync(string selector, CancellationToken cancellationToken = default) =>
        Task.FromResult(_committed.FirstOrDefault(key => key.Selector == selector).ToOption());

    public Task AddAsync(SdkKey key, CancellationToken cancellationToken = default)
    {
        _pending.Add(key);

        return Task.CompletedTask;
    }

    public Task<Result> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;

        _committed.AddRange(_pending);
        _pending.Clear();

        return Task.FromResult(Result.Success());
    }
}
