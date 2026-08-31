using FutureFlags.Domain.Flags;
using FutureFlags.Domain.Shared;

namespace FutureFlags.Server.Tests.Fakes;

/// <summary>
/// In-memory stand-in for the EF repository. Tracks added flags and save calls so tests can
/// assert the handler persists exactly once and only on the success path.
/// <para>
/// Lives outside Features/ because it stands in for a Domain interface that every flag slice
/// depends on — copying it into each slice would make three of them drift.
/// </para>
/// </summary>
internal sealed class FakeFeatureFlagRepository : IFeatureFlagRepository
{
    private readonly Dictionary<FlagKey, FeatureFlag> _committed = [];
    private readonly List<FeatureFlag> _pending = [];

    private Error? _nextSaveFailure;

    public int SaveChangesCallCount { get; private set; }

    public IReadOnlyCollection<FeatureFlag> Committed => _committed.Values;

    public void Seed(FeatureFlag flag) => _committed[flag.Key] = flag;

    /// <summary>
    /// Makes the next save report a store-detected conflict, standing in for another writer
    /// taking the key between the handler's check and its insert.
    /// </summary>
    public void FailNextSaveWith(Error error) => _nextSaveFailure = error;

    public Task<Option<FeatureFlag>> GetByKeyAsync(FlagKey key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_committed.TryGetValue(key, out var flag)
            ? Option<FeatureFlag>.Some(flag)
            : Option<FeatureFlag>.None);

    public Task AddAsync(FeatureFlag flag, CancellationToken cancellationToken = default)
    {
        _pending.Add(flag);
        return Task.CompletedTask;
    }

    public Task<Result> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;

        if (_nextSaveFailure is { } failure)
        {
            _nextSaveFailure = null;
            _pending.Clear();

            return Task.FromResult(Result.Failure(failure));
        }

        foreach (var flag in _pending)
            _committed[flag.Key] = flag;

        _pending.Clear();

        return Task.FromResult(Result.Success());
    }
}
