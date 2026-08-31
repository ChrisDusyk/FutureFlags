using FutureFlags.Domain.Segments;
using FutureFlags.Domain.Segments.Events;
using FutureFlags.Domain.Shared;

namespace FutureFlags.Server.Tests.Fakes;

/// <summary>
/// In-memory stand-in for the segment read side.
///
/// <para>
/// A retired segment models the real repository's tombstone rather than merely being absent: it is
/// seeded, then <see cref="Retire"/> marks it, and every method here except
/// <see cref="GetHistoryAsync"/> then excludes it — matching
/// <c>SegmentViewRepository</c>'s <c>WHERE DeletedAt == null</c>. A key nobody ever seeded and a
/// key that was seeded and retired both read as "not found" from the outside, but a test asserting
/// the second case should actually retire one rather than simply never seed it, or the test is
/// passing for the wrong reason.
/// </para>
/// </summary>
internal sealed class FakeSegmentViewRepository : ISegmentViewRepository
{
    private readonly Dictionary<SegmentKey, SegmentView> _views = [];
    private readonly HashSet<SegmentKey> _retired = [];
    private readonly Dictionary<Guid, List<ISegmentEvent>> _histories = [];

    public void Seed(SegmentView view) => _views[view.Key] = view;

    /// <summary>Seeds a segment by key alone, for the callers that only ever ask whether it exists.</summary>
    public void Seed(SegmentKey key) => Seed(new SegmentView(
        Guid.CreateVersion7(), key, key.Value, string.Empty, SegmentDefinition.Empty,
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

    /// <summary>Tombstones a previously seeded segment, the way <c>Segment.Delete</c> does at the
    /// write side. The key stays known to <see cref="GetHistoryAsync"/> but disappears from
    /// everything else, same as the real repository.</summary>
    public void Retire(SegmentKey key) => _retired.Add(key);

    /// <summary>Sets the events <see cref="GetHistoryAsync"/> returns for a segment id, newest
    /// first — matching the real repository's <c>ORDER BY SequenceNumber DESC</c>.</summary>
    public void SeedHistory(Guid segmentId, params ISegmentEvent[] events) => _histories[segmentId] = [.. events];

    public Task<IReadOnlyList<SegmentView>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SegmentView>>(
            [.. _views.Values
                .Where(view => !_retired.Contains(view.Key))
                .OrderBy(view => view.Key.Value, StringComparer.Ordinal)]);

    public Task<Option<SegmentView>> GetByKeyAsync(SegmentKey key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_views.TryGetValue(key, out var view) && !_retired.Contains(key)
            ? Option<SegmentView>.Some(view)
            : Option<SegmentView>.None);

    public Task<IReadOnlyList<SegmentKey>> FilterExistingAsync(
        IReadOnlyCollection<SegmentKey> keys,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SegmentKey>>(
            [.. keys.Distinct().Where(key => _views.ContainsKey(key) && !_retired.Contains(key))]);

    public Task<IReadOnlyList<ISegmentEvent>> GetHistoryAsync(
        Guid segmentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ISegmentEvent>>(
            _histories.TryGetValue(segmentId, out var events) ? events : []);
}
