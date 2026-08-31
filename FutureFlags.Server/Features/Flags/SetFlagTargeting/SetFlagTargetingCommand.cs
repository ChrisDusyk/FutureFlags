using FutureFlags.Domain.Environments;
using FutureFlags.Domain.Flags;
using FutureFlags.Domain.Segments;

namespace FutureFlags.Server.Features.Flags.SetFlagTargeting;

/// <summary>
/// Replaces the segments a flag reaches in one environment. Replacing, not adding — the caller
/// sends the set it wants, so sending the same request twice is the same as sending it once.
/// </summary>
public sealed record SetFlagTargetingCommand(
    FlagKey Key,
    EnvironmentKey Environment,
    IReadOnlyList<SegmentKey> Segments,
    Guid CausedBy);

/// <summary>The wire shape. <see cref="SetFlagTargetingCommand"/> is what survives validation.</summary>
public sealed record SetFlagTargetingRequest(string? Environment, IReadOnlyList<string>? Segments);
