using System.Text.Json;
using FutureFlags.Domain.Environments;
using FutureFlags.Domain.Flags;
using FutureFlags.Evaluation;

namespace FutureFlags.Server.Features.Evaluation.OfrepEvaluateFlag;

public sealed record OfrepEvaluateFlagQuery(EnvironmentKey Environment, FlagKey Key, FlagContext Context);

/// <inheritdoc cref="OfrepEvaluateFlags.OfrepEvaluateFlagsRequest"/>
public sealed record OfrepEvaluateFlagRequest(IReadOnlyDictionary<string, JsonElement>? Context);
