using FeatureFlags.Domain.SdkKeys;
using FeatureFlags.Domain.Shared;
using FeatureFlags.Evaluation;
using FeatureFlags.Server.Api;
using FeatureFlags.Server.Evaluation;

namespace FeatureFlags.Server.Features.Evaluation.GetRuleset;

public static class GetRulesetEndpoint
{
    public static IEndpointRouteBuilder MapGetRuleset(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/evaluation/ruleset", async (
            HttpContext context,
            GetRulesetHandler handler,
            CancellationToken cancellationToken) =>
        {
            // Two independent refusals, and neither substitutes for the other. A secret key
            // presented from a browser is refused whatever it asked for; a publishable key is
            // refused here whether or not a browser sent it, because this route ships segment
            // definitions and a publishable key is expected to be readable by anyone.
            var browser = BrowserCredentialRule.Check(context);
            if (browser.IsFailure)
            {
                return browser.Error.ToProblem();
            }

            var secret = SecretCredentialRule.RequireSecret(context);
            if (secret.IsFailure)
            {
                return secret.Error.ToProblem();
            }

            // The policy already guarantees an SDK key authenticated this request, so a missing
            // environment claim is this server contradicting itself rather than a caller's mistake.
            var environment = context.User.GetSdkKeyEnvironment()
                .ToResult(SdkKeyErrors.TokenMalformed);

            if (environment.IsFailure)
            {
                return environment.Error.ToProblem();
            }

            var result = await handler.HandleAsync(new GetRulesetQuery(environment.Value), cancellationToken);

            return result.Match(
                cached => ConditionalResponse.Respond(context, cached.ETag, cached.Ruleset),
                error => error.ToProblem());
        })
        .RequireAuthorization(AuthPolicies.SdkKey)
        // Deliberately no RequireCors. This route is not for browsers at all, and without a CORS
        // policy a preflight to it fails and the browser blocks the request — which is the outcome
        // we want, arrived at by the browser's own rules rather than by ours.
        .WithName("GetRuleset")
        .WithSummary("Every flag and the segments it reaches, for a secret SDK key to evaluate itself.")
        .Produces<Ruleset>()
        .Produces(StatusCodes.Status304NotModified)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }
}
