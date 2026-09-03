using Microsoft.AspNetCore.OpenApi;

namespace FutureFlags.Server.Api;

/// <summary>
/// Marks a route as deprecated to its callers as well as to its documentation.
///
/// <para>
/// <c>ObsoleteAttribute</c> metadata is what <c>AddOpenApi()</c> reads to flag an operation in the
/// published document, which reaches whoever generates a client. It reaches nobody already running
/// one. RFC 8594's <c>Deprecation</c> header does — it turns up in logs and in a proxy's response
/// view, where somebody operating a deployed application will actually see it.
/// </para>
/// <para>
/// No <c>Sunset</c>: that header states a date these routes will stop answering, and no such date
/// has been chosen. Sending one we do not mean would be worse than sending none.
/// </para>
/// </summary>
public static class DeprecatedRoute
{
    /// <summary>The successor route, so a caller reading the header knows where to go.</summary>
    public const string LinkHeader = "<https://github.com/ChrisDusyk/FutureFlags>; rel=\"deprecation\"";

    public static TBuilder MarkDeprecated<TBuilder>(this TBuilder builder, string successor)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpoint => endpoint.Metadata.Add(new ObsoleteAttribute(successor)));

        return builder.AddEndpointFilterFactory((_, next) => async invocation =>
        {
            var response = invocation.HttpContext.Response;

            // Set before the handler runs, so it is present on a 304 as well as on a body.
            response.Headers["Deprecation"] = "true";
            response.Headers["Link"] = LinkHeader;

            return await next(invocation);
        });
    }

    /// <summary>
    /// Carries <see cref="MarkDeprecated"/>'s metadata into the published document.
    ///
    /// <para>
    /// Needed because <c>AddOpenApi()</c> reads <c>ObsoleteAttribute</c> from the handler's own
    /// method, not from endpoint metadata — and a minimal-API lambda has nowhere to put an
    /// attribute. Without this the routes are marked deprecated to a caller at run time and to
    /// nobody reading the OpenAPI document, which is the audience most likely to act on it. Checked
    /// rather than assumed: metadata alone left <c>deprecated</c> unset in the generated document.
    /// </para>
    /// </summary>
    public static OpenApiOptions MarkDeprecatedOperations(this OpenApiOptions options) =>
        options.AddOperationTransformer((operation, context, _) =>
        {
            if (context.Description.ActionDescriptor.EndpointMetadata.OfType<ObsoleteAttribute>().Any())
            {
                operation.Deprecated = true;
            }

            return Task.CompletedTask;
        });
}
