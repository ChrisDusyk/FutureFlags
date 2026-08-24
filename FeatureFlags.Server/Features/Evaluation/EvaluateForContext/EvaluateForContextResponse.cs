namespace FeatureFlags.Server.Features.Evaluation.EvaluateForContext;

/// <summary>
/// What a client needs to answer "is this on for this person", and nothing else.
///
/// <para>
/// Deliberately the same <c>environment</c>/<c>flags</c> shape <c>GET /api/evaluation</c> answers
/// with, so a client already parsing one needs no second parser for the other.
/// </para>
/// <para>
/// <see cref="RulesetVersion"/> is the one addition: the tag for the ruleset this answer was
/// computed from. It is in the body rather than in an <c>ETag</c> header on purpose. RFC 9110
/// says a false <c>If-None-Match</c> on anything other than GET or HEAD must answer 412, not 304,
/// so a conditional POST would be a private protocol wearing HTTP's clothes — and the saving would
/// be a few hundred gzipped bytes. In the body, a client can cache by
/// <c>(rulesetVersion, context)</c> and skip its own recomputation, which is the part that actually
/// costs something.
/// </para>
/// </summary>
public sealed record EvaluateForContextResponse(
    string Environment,
    string RulesetVersion,
    IReadOnlyDictionary<string, bool> Flags);
