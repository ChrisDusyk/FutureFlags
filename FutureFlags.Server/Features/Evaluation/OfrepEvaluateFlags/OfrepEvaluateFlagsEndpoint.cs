using System.Text.Json;
using FutureFlags.Domain.SdkKeys;
using FutureFlags.Domain.Shared;
using FutureFlags.Server.Api;
using FutureFlags.Server.Evaluation;
using Microsoft.AspNetCore.Mvc;

namespace FutureFlags.Server.Features.Evaluation.OfrepEvaluateFlags;

/// <summary>
/// <c>POST /ofrep/v1/evaluate/flags</c> — the OpenFeature Remote Evaluation Protocol's bulk route.
///
/// <para>
/// This is the route that makes FutureFlags reachable from any OpenFeature SDK in any language,
/// with no FutureFlags-specific code on the client at all. It is the successor to both
/// <c>GET /api/evaluation</c> and <c>POST /api/evaluation</c>, which keep answering unchanged.
/// </para>
/// <para>
/// Either kind of SDK key is accepted, unlike the ruleset route: this answers with values, never
/// with segment definitions, so there is nothing here a publishable key must not see.
/// <see cref="BrowserCredentialRule"/> still applies — a <em>secret</em> key arriving from a browser
/// has already been published, whatever this particular request returns.
/// </para>
/// </summary>
public static class OfrepEvaluateFlagsEndpoint
{
    public static IEndpointRouteBuilder MapOfrepEvaluateFlags(this IEndpointRouteBuilder endpoints)
    {
        // The lambda's return type is spelled out because its branches mix ProblemDetails with
        // OFREP's own failure shapes, and the compiler will not infer a common type across them.
        endpoints.MapPost("/v1/evaluate/flags", async Task<IResult> (
            HttpContext context,
            OfrepEvaluateFlagsHandler handler,
            CancellationToken cancellationToken) =>
        {
            // Read by hand rather than bound, for the reason spelled out on
            // EvaluateForContextEndpoint: a bound complex parameter is deserialized before the
            // delegate runs, so a refused credential would still pay for the JSON parse.
            var credential = BrowserCredentialRule.Check(context);
            if (credential.IsFailure)
            {
                return credential.Error.ToProblem();
            }

            var environment = context.User.GetSdkKeyEnvironment().ToResult(SdkKeyErrors.TokenMalformed);
            if (environment.IsFailure)
            {
                return environment.Error.ToProblem();
            }

            OfrepEvaluateFlagsRequest? request;

            try
            {
                request = await context.Request.ReadFromJsonAsync<OfrepEvaluateFlagsRequest>(cancellationToken);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new OfrepFailure(
                    OfrepErrors.MalformedBody.Code,
                    OfrepErrors.MalformedBody.Message));
            }
            catch (BadHttpRequestException exception)
            {
                return Results.StatusCode(exception.StatusCode);
            }

            var evaluationContext = Ofrep.BindContext(request?.Context);
            if (evaluationContext.IsFailure)
            {
                return Results.BadRequest(new OfrepFailure(
                    evaluationContext.Error.Code,
                    evaluationContext.Error.Message));
            }

            var result = await handler.HandleAsync(
                new OfrepEvaluateFlagsQuery(environment.Value, evaluationContext.Value),
                cancellationToken);

            return result.Match(
                // Conditional on a POST, which this platform's own POST /api/evaluation
                // deliberately is not — that route puts its version in the body because RFC 9110
                // says a failed If-None-Match on a POST must answer 412 rather than 304. OFREP
                // specifies 304 here outright, and interoperating with clients that expect it is
                // the whole point of the route, so the protocol wins. The tag folds the context in
                // as well as the ruleset, so a client that changed its context is never told
                // nothing changed.
                evaluated => ConditionalResponse.Respond(context, evaluated.ETag, evaluated.Response),
                error => error.ToProblem());
        })
        .RequireAuthorization(AuthPolicies.SdkKey)
        .RequireCors(BrowserOrigins.PolicyName)
        .WithMetadata(new RequestSizeLimitAttribute(Ofrep.MaxRequestBytes))
        .WithName("OfrepEvaluateFlags")
        .WithSummary("Every flag's value for one context, in the OpenFeature Remote Evaluation Protocol's bulk shape.")
        .Produces<OfrepEvaluateFlagsResponse>()
        .Produces(StatusCodes.Status304NotModified)
        .Produces<OfrepFailure>(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        return endpoints;
    }
}
