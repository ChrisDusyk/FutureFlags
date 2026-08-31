using FutureFlags.Domain.Flags;

namespace FutureFlags.Server.Features.Flags.UpdateFlag;

public sealed record UpdateFlagCommand(FlagKey Key, string? Name, string? Description, Guid CausedBy);

/// <summary>
/// The wire shape. No <c>Key</c> property — the route segment is the only source of identity, so
/// there is nothing here a caller could send to rename a flag.
/// </summary>
public sealed record UpdateFlagRequest(string? Name, string? Description);
