using FeatureFlags.Domain.Environments;
using FeatureFlags.Domain.Flags;

namespace FeatureFlags.Server.Features.Flags.SetFlagTargeting;

/// <summary>
/// The environment's state after the change. <see cref="IsEnabled"/> is here as well as
/// <see cref="TargetedSegments"/> because the two together are the answer — a flag that is off
/// reaches nobody whatever it targets, and a screen showing only half of that would mislead.
/// </summary>
public sealed record SetFlagTargetingResponse(
    string Key,
    string Environment,
    bool IsEnabled,
    IReadOnlyList<string> TargetedSegments,
    DateTimeOffset UpdatedAt)
{
    public static SetFlagTargetingResponse From(FeatureFlag flag, EnvironmentKey environment) =>
        flag.StateIn(environment).Match(
            state => new SetFlagTargetingResponse(
                flag.Key.Value,
                environment.Value,
                state.IsEnabled,
                [.. state.TargetedSegments.Select(segment => segment.Value)],
                state.UpdatedAt),
            () => new SetFlagTargetingResponse(flag.Key.Value, environment.Value, false, [], flag.UpdatedAt));
}
