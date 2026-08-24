namespace FeatureFlags.Server.Api;

/// <summary>
/// The origins a browser application may read <c>/api/evaluation</c> from.
///
/// <para>
/// Empty by default, and that is the right default: an installation whose flags are only read by
/// server-side code should not be answering cross-origin requests at all. Adding an origin here is
/// how an operator says "a web application of mine lives there" — it is not a security boundary on
/// its own, because CORS is enforced by the browser rather than by us, but it is the statement of
/// intent that the boundary is built on.
/// </para>
///
/// <para>
/// What <em>is</em> enforced here is the key kind: see <c>EvaluateFlagsEndpoint</c>. A request
/// carrying an <c>Origin</c> header came from a browser, and a secret key is refused from one no
/// matter which origin sent it.
/// </para>
/// </summary>
public static class BrowserOrigins
{
    /// <summary>The CORS policy name the evaluation endpoint asks for.</summary>
    public const string PolicyName = "browser-evaluation";

    public const string ConfigurationKey = "Cors:BrowserOrigins";

    /// <summary>
    /// The configured origins, or empty. Each is compared whole and exactly — scheme, host, and
    /// port — which is what an <c>Origin</c> header actually contains.
    /// </summary>
    public static string[] GetBrowserOrigins(this IConfiguration configuration) =>
        configuration.GetSection(ConfigurationKey).Get<string[]>()
        ?? [];

    public static IServiceCollection AddBrowserCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var origins = configuration.GetBrowserOrigins();

        services.AddCors(options => options.AddPolicy(PolicyName, policy =>
        {
            if (origins.Length == 0)
            {
                // No origins configured means no browser is expected. A policy that allows nothing
                // is the honest expression of that — the endpoint keeps working for server-side
                // callers, which send no Origin and so are never subject to CORS at all.
                return;
            }

            policy.WithOrigins(origins)
                // POST as well as GET: a browser client that describes a user posts that context
                // to /api/evaluation and gets booleans back, because segment definitions have no
                // business being in a bundle. See EvaluateForContextEndpoint.
                .WithMethods(HttpMethods.Get, HttpMethods.Post)
                // What the client actually sends: the credential, the cache validator that makes a
                // poll cheap, and — for the POST — the content type, without which the preflight
                // fails before the request is ever made.
                .WithHeaders("Authorization", "If-None-Match", "Content-Type")
                // Without this the browser hides the ETag from the script that has to send it back,
                // and every poll would fetch a full body forever.
                .WithExposedHeaders("ETag");
        }));

        return services;
    }
}
