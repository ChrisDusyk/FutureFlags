using System.Text.Json;
using FutureFlags.Domain.Environments;
using FutureFlags.Evaluation;

namespace FutureFlags.Server.Features.Evaluation.OfrepEvaluateFlags;

public sealed record OfrepEvaluateFlagsQuery(EnvironmentKey Environment, FlagContext Context);

/// <summary>
/// The wire shape. OpenFeature's context is flat — <c>targetingKey</c> alongside arbitrary custom
/// fields — so it is read as raw JSON and bound by <see cref="Server.Evaluation.Ofrep.BindContext"/>
/// rather than by a typed record: a field whose value is an object or an array is a context this
/// platform cannot represent, and it has to be dropped rather than fail the whole request.
/// </summary>
public sealed record OfrepEvaluateFlagsRequest(IReadOnlyDictionary<string, JsonElement>? Context);
