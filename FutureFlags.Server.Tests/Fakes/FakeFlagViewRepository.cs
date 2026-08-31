using FutureFlags.Domain.Environments;
using FutureFlags.Domain.Flags;
using FutureFlags.Domain.Flags.Events;
using FutureFlags.Domain.Segments;
using FutureFlags.Domain.Shared;

namespace FutureFlags.Server.Tests.Fakes;

/// <summary>
/// In-memory stand-in for the read-model repository. <see cref="FlagView"/> is immutable, so unlike
/// <see cref="FakeFeatureFlagRepository"/> — where a test can mutate a seeded aggregate directly —
/// a test that wants to see a flag change after seeding goes through <see cref="SetEnabled"/>, which
/// replaces the stored view the way a write elsewhere in the system would have.
/// </summary>
internal sealed class FakeFlagViewRepository : IFlagViewRepository
{
    private readonly Dictionary<FlagKey, FlagView> _views = [];
    private readonly Dictionary<Guid, List<IFlagEvent>> _histories = [];

    public void Seed(FlagView view) => _views[view.Key] = view;

    /// <summary>Sets the events <see cref="GetHistoryAsync"/> returns for a flag id, newest first —
    /// matching what the real repository returns from its <c>ORDER BY SequenceNumber DESC</c>.</summary>
    public void SeedHistory(Guid flagId, params IFlagEvent[] events) => _histories[flagId] = [.. events];

    public void SetEnabled(FlagKey key, EnvironmentKey environment, bool isEnabled, DateTimeOffset updatedAt)
    {
        var view = _views[key];

        var states = view.States
            .Select(state => state.Environment == environment
                ? state with { IsEnabled = isEnabled, UpdatedAt = updatedAt }
                : state)
            .ToList();

        _views[key] = view with { States = states };
    }

    /// <summary>Points a flag's environment at some segments, the way SetFlagTargeting would.</summary>
    public void SetTargeting(FlagKey key, EnvironmentKey environment, IReadOnlyList<SegmentKey> segments, DateTimeOffset updatedAt)
    {
        var view = _views[key];

        var states = view.States
            .Select(state => state.Environment == environment
                ? state with { TargetedSegments = segments, UpdatedAt = updatedAt }
                : state)
            .ToList();

        _views[key] = view with { States = states };
    }

    public Task<IReadOnlyList<FlagView>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FlagView>>(
            [.. _views.Values.OrderBy(view => view.Key.Value, StringComparer.Ordinal)]);

    public Task<Option<FlagView>> GetByKeyAsync(FlagKey key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_views.TryGetValue(key, out var view)
            ? Option<FlagView>.Some(view)
            : Option<FlagView>.None);

    public Task<IReadOnlyList<IFlagEvent>> GetHistoryAsync(Guid flagId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IFlagEvent>>(
            _histories.TryGetValue(flagId, out var events) ? events : []);

    public Task<IReadOnlyList<FlagTargetingView>> ListTargetingAsync(
        SegmentKey segment,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FlagTargetingView>>(
        [
            .. _views.Values
                .SelectMany(view => view.States
                    .Where(state => state.TargetedSegments.Contains(segment))
                    .Select(state => new FlagTargetingView(view.Key, view.Name, state.Environment)))
                .OrderBy(view => view.Key.Value, StringComparer.Ordinal)
                .ThenBy(view => view.Environment.Ordinal),
        ]);
}
