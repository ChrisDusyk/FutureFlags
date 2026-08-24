using FeatureFlags.Domain.Environments;
using FeatureFlags.Evaluation;

namespace FeatureFlags.Server.Features.Evaluation.EvaluateForContext;

public sealed record EvaluateForContextQuery(EnvironmentKey Environment, FlagContext Context);

/// <summary>The wire shape. <see cref="EvaluateForContextQuery"/> is what survives validation.</summary>
public sealed record EvaluateForContextRequest(EvaluateForContextContextRequest? Context);

/// <summary>
/// Who is being asked about.
///
/// <para>
/// <see cref="Key"/> is optional — an application that has not identified anybody still gets an
/// answer, and every segment's include and exclude list simply cannot match. <see cref="Attributes"/>
/// values are bare JSON primitives, because JSON's own types are exactly the three an attribute can
/// hold; see <c>AttributeValue</c>.
/// </para>
/// </summary>
public sealed record EvaluateForContextContextRequest(
    string? Key,
    IReadOnlyDictionary<string, AttributeValue>? Attributes);
