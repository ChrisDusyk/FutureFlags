using FeatureFlags.Domain.Segments;
using FeatureFlags.Domain.Shared;

namespace FeatureFlags.Server.Tests.Fakes;

/// <summary>
/// In-memory stand-in for the segment write side, mirroring <see cref="FakeFeatureFlagRepository"/>.
///
/// <para>
/// Retired segments stay in <see cref="_committed"/> rather than being removed, because the real
/// repository deliberately still returns them — that is how a caller tells "this key is taken" from
/// "this key will never be reissued".
/// </para>
/// </summary>
internal sealed class FakeSegmentRepository : ISegmentRepository
{
    private readonly Dictionary<SegmentKey, Segment> _committed = [];
    private readonly List<Segment> _pending = [];

    private Error? _nextSaveFailure;

    public int SaveChangesCallCount { get; private set; }

    public IReadOnlyCollection<Segment> Committed => _committed.Values;

    public void Seed(Segment segment) => _committed[segment.Key] = segment;

    /// <summary>Makes the next save report a store-detected conflict, standing in for another
    /// writer taking the key between the handler's check and its insert.</summary>
    public void FailNextSaveWith(Error error) => _nextSaveFailure = error;

    public Task<Option<Segment>> GetByKeyAsync(SegmentKey key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_committed.TryGetValue(key, out var segment)
            ? Option<Segment>.Some(segment)
            : Option<Segment>.None);

    public Task AddAsync(Segment segment, CancellationToken cancellationToken = default)
    {
        _pending.Add(segment);
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

        foreach (var segment in _pending)
            _committed[segment.Key] = segment;

        _pending.Clear();

        return Task.FromResult(Result.Success());
    }
}
