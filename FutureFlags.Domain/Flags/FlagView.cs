using FutureFlags.Domain.Environments;
using FutureFlags.Domain.Shared;

namespace FutureFlags.Domain.Flags;

/// <summary>
/// The current state of a flag, projected for fast reading. Unlike <see cref="FeatureFlag"/> this
/// is not sourced from events at read time and carries no history — it is what the write side's
/// events most recently produced, kept for a query to answer quickly rather than by replay.
/// </summary>
public sealed record FlagView(
    Guid Id,
    FlagKey Key,
    string Name,
    string Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<FlagStateView> States)
{
    public bool IsEnabledIn(EnvironmentKey environment) =>
        StateIn(environment).Match(state => state.IsEnabled, () => false);

    public Option<FlagStateView> StateIn(EnvironmentKey environment) =>
        States.FirstOrDefault(state => state.Environment == environment).ToOption();
}
