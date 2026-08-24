using FeatureFlags.Domain.Environments;
using FeatureFlags.Domain.Flags;

namespace FeatureFlags.Server.Features.Flags.ListFlags;

/// <summary>
/// A flag as one environment sees it. <see cref="IsEnabled"/>, <see cref="TargetedSegmentCount"/>,
/// and <see cref="UpdatedAt"/> all come from that environment's state, so switching environments
/// genuinely changes the answer.
///
/// <para>
/// A count rather than the keys themselves: a list needs to say "on, for 2 segments" — which is a
/// different claim from "on" and has to be visible without opening the flag — and naming them is
/// the detail screen's job.
/// </para>
/// </summary>
public sealed record FlagSummary(
    Guid Id,
    string Key,
    string Name,
    string Description,
    bool IsEnabled,
    int TargetedSegmentCount,
    DateTimeOffset UpdatedAt)
{
    public static FlagSummary From(FlagView flag, EnvironmentKey environment) =>
        flag.StateIn(environment).Match(
            state => new FlagSummary(
                flag.Id,
                flag.Key.Value,
                flag.Name,
                flag.Description,
                state.IsEnabled,
                state.TargetedSegments.Count,
                state.UpdatedAt),
            // A flag with no state for an environment cannot happen while the set is fixed. Report
            // it as off and last touched when the flag was, rather than dropping the row: a flag
            // vanishing from the list is a worse lie than a flag shown off.
            () => new FlagSummary(
                flag.Id,
                flag.Key.Value,
                flag.Name,
                flag.Description,
                false,
                0,
                flag.UpdatedAt));
}

public sealed record ListFlagsResponse(string Environment, IReadOnlyList<FlagSummary> Flags);
