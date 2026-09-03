using FutureFlags.Domain.Environments;
using FutureFlags.Domain.Shared;
using FutureFlags.Evaluation;

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
    /// <summary>
    /// What kind of value this flag serves. An init-only property with a default rather than a
    /// positional parameter, so a row written before variants existed — and every caller that only
    /// cares whether the flag is on — reads the boolean shape without spelling it out.
    /// </summary>
    public FlagValueType ValueType { get; init; } = FlagValueType.Boolean;

    /// <summary>The named values this flag can serve. Defaulted on the same terms as
    /// <see cref="ValueType"/>.</summary>
    public FlagVariants Variants { get; init; } = FlagVariants.BooleanPair;

    public bool IsEnabledIn(EnvironmentKey environment) =>
        StateIn(environment).Match(state => state.IsEnabled, () => false);

    public Option<FlagStateView> StateIn(EnvironmentKey environment) =>
        States.FirstOrDefault(state => state.Environment == environment).ToOption();
}
