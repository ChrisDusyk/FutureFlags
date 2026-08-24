using FeatureFlags.Domain.SdkKeys;
using FeatureFlags.Domain.Shared;
using FeatureFlags.Server.Api;
using FeatureFlags.Server.Evaluation;

namespace FeatureFlags.Server.Features.Flags.EvaluateFlags;

public static class EvaluateFlagsEndpoint
{
    public static IEndpointRouteBuilder MapEvaluateFlags(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/evaluation", async (
            HttpContext context,
            EvaluateFlagsHandler handler,
            CancellationToken cancellationToken) =>
        {
            // Before anything is read: a secret key presented from a browser is refused outright,
            // whatever it was asking for. See BrowserCredentialRule.
            var credential = BrowserCredentialRule.Check(context);

            if (credential.IsFailure)
            {
                return credential.Error.ToProblem();
            }

            // The policy already guarantees an SDK key authenticated this request, so a missing
            // environment claim is this server contradicting itself rather than a caller's mistake.
            // It still has to be answered for rather than assumed, which is what Option is for.
            var environment = context.User.GetSdkKeyEnvironment()
                .ToResult(SdkKeyErrors.TokenMalformed);

            if (environment.IsFailure)
            {
                return environment.Error.ToProblem();
            }

            var result = await handler.HandleAsync(
                new EvaluateFlagsQuery(environment.Value),
                cancellationToken);

            return result.Match(
                evaluated => ConditionalResponse.Respond(context, evaluated.ETag, evaluated.Response),
                error => error.ToProblem());
        })
        .RequireAuthorization(AuthPolicies.SdkKey)
        .RequireCors(BrowserOrigins.PolicyName)
        .WithName("EvaluateFlags")
        .WithSummary("Every flag's state in the environment the presented SDK key is scoped to, for nobody in particular.")
        .Produces<EvaluateFlagsResponse>()
        .Produces(StatusCodes.Status304NotModified)
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        return endpoints;
    }
}
