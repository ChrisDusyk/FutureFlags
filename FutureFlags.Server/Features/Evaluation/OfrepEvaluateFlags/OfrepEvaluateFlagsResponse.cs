using FutureFlags.Evaluation;
using FutureFlags.Server.Evaluation;

namespace FutureFlags.Server.Features.Evaluation.OfrepEvaluateFlags;

/// <summary>
/// Every flag in the environment, answered for one context, in OFREP's bulk shape.
///
/// <para>
/// An array of results rather than the key-to-boolean map this platform's own routes answer with,
/// because each entry now carries a value, a variant and a reason. That is the shape any
/// OpenFeature provider already knows how to read, which is the entire point of the route.
/// </para>
/// </summary>
public sealed record OfrepEvaluateFlagsResponse(
    IReadOnlyList<OfrepFlagResult> Flags,
    IReadOnlyDictionary<string, AttributeValue> Metadata);

/// <summary>The response and the tag identifying this exact set of answers.</summary>
public sealed record OfrepEvaluatedFlags(OfrepEvaluateFlagsResponse Response, string ETag);
