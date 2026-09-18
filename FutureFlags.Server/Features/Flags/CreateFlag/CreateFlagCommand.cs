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
    Guid CausedBy,
    string? ValueType = null);

/// <summary>The wire shape. <see cref="CreateFlagCommand"/> is what survives validation.</summary>
/// <param name="ValueType">
/// One of <c>boolean</c>, <c>string</c>, <c>number</c>, <c>object</c>. Optional, and null means
/// boolean — which is every flag this build can author. The others are named on the wire so a
/// caller asking for one is told that it is not supported yet rather than that it is not a type,
/// which are different facts.
/// </param>
public sealed record CreateFlagRequest(
    string? Key,
    string? Name,
    string? Description,
    IReadOnlyList<string>? EnabledIn,
    string? ValueType = null);
