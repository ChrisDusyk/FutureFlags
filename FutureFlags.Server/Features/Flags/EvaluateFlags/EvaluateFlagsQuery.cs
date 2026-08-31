using FutureFlags.Domain.Environments;

namespace FutureFlags.Server.Features.Flags.EvaluateFlags;

/// <summary>
/// Every flag's state in one environment, for a program rather than a person.
///
/// <para>
/// The environment comes from the SDK key that authenticated the request, which is why it is the
/// only thing here. A caller cannot name an environment, so a caller cannot name the wrong one.
/// </para>
/// </summary>
public sealed record EvaluateFlagsQuery(EnvironmentKey Environment);
