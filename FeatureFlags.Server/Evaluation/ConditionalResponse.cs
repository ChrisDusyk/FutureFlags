using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace FeatureFlags.Server.Evaluation;

/// <summary>
/// The conditional-GET plumbing the evaluation routes share.
///
/// <para>
/// Lifted out of <c>EvaluateFlagsEndpoint</c> when a second route needed it. Two copies of an
/// <c>If-None-Match</c> parser is two chances to get weak comparison or a comma-separated list
/// subtly wrong, in a header whose whole job is to decide whether a caller is told nothing changed.
/// </para>
/// </summary>
public static class ConditionalResponse
{
    /// <summary>
    /// Sets the cache headers and answers with the body, or with a 304 when the caller already has it.
    ///
    /// <para>
    /// <c>no-cache</c> is not "do not cache" — it is "hold it, but ask before using it", which is
    /// exactly the arrangement an ETag sets up. <c>private</c> because the answer depends on the key
    /// that asked, and a shared proxy serving one environment's flags to another environment's
    /// client would be the worst kind of bug to find.
    /// </para>
    /// </summary>
    public static IResult Respond<TBody>(HttpContext context, string etag, TBody body)
    {
        context.Response.Headers.CacheControl = "no-cache, private";
        context.Response.Headers.ETag = etag;

        return IsUnchanged(context.Request.Headers[HeaderNames.IfNoneMatch], etag)
            ? Results.StatusCode(StatusCodes.Status304NotModified)
            : Results.Ok(body);
    }

    /// <summary>
    /// Whether the caller already holds this version. <c>If-None-Match</c> is a comma-separated list
    /// and may repeat as a header, so both are flattened; <c>*</c> means "any version I have". A
    /// weak-comparison prefix is accepted because this is a cache validator, not a range request —
    /// nothing here distinguishes a weak tag from a strong one.
    /// </summary>
    public static bool IsUnchanged(StringValues presented, string etag) =>
        presented
            .SelectMany(value => value?.Split(',') ?? [])
            .Select(candidate => candidate.Trim())
            .Any(candidate =>
                candidate == "*" ||
                candidate == etag ||
                (candidate.StartsWith("W/", StringComparison.Ordinal) && candidate[2..] == etag));
}
