using FutureFlags.Domain.Environments;
using FutureFlags.Domain.Flags;

namespace FutureFlags.Server.Features.Flags.ToggleFlag;

/// <summary>
/// Sets a flag's state in one environment. Setting, not flipping — the caller says what it wants
/// the state to be, so sending the same request twice is the same as sending it once.
/// </summary>
public sealed record ToggleFlagCommand(FlagKey Key, EnvironmentKey Environment, bool IsEnabled, Guid CausedBy);

/// <summary>The wire shape. <see cref="ToggleFlagCommand"/> is what survives validation.</summary>
public sealed record ToggleFlagRequest(string? Environment, bool IsEnabled);
