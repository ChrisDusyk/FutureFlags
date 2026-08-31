using FutureFlags.Domain.Environments;

namespace FutureFlags.Server.Features.Flags.CreateFlag;

/// <summary>
/// A flag is created in every environment at once. <paramref name="EnabledIn"/> names the ones it
/// starts on in — everywhere else it starts off, which is the only safe default for a key nothing
/// has been tested against yet.
/// </summary>
public sealed record CreateFlagCommand(
    string? Key,
    string? Name,
    string? Description,
    IReadOnlyList<EnvironmentKey> EnabledIn,
    Guid CausedBy);

/// <summary>The wire shape. <see cref="CreateFlagCommand"/> is what survives validation.</summary>
public sealed record CreateFlagRequest(
    string? Key,
    string? Name,
    string? Description,
    IReadOnlyList<string>? EnabledIn);
