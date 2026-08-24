using FeatureFlags.Domain.Environments;

namespace FeatureFlags.Server.Features.Evaluation.GetRuleset;

/// <summary>
/// Everything a server-side client needs to answer for itself, in one environment.
///
/// <para>
/// The environment comes from the SDK key that authenticated the request, which is why it is the
/// only thing here — a caller cannot name an environment, so a caller cannot name the wrong one.
/// </para>
/// </summary>
public sealed record GetRulesetQuery(EnvironmentKey Environment);
