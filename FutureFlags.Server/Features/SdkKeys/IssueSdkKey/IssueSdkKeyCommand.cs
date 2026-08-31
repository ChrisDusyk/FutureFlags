using FutureFlags.Domain.Environments;
using FutureFlags.Domain.SdkKeys;

namespace FutureFlags.Server.Features.SdkKeys.IssueSdkKey;

/// <summary>
/// Issues a key for one environment on behalf of the admin who asked.
/// <see cref="IssuedBy"/> comes from the caller's token, never from the request body.
/// </summary>
public sealed record IssueSdkKeyCommand(
    string? Name,
    SdkKeyKind Kind,
    EnvironmentKey Environment,
    Guid IssuedBy);
