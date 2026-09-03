using System.Text.Json;
using FutureFlags.Domain.Flags;
using FutureFlags.Domain.SdkKeys;
using FutureFlags.Domain.Shared;
using FutureFlags.Server.Api;
using FutureFlags.Server.Evaluation;
using Microsoft.AspNetCore.Mvc;

namespace FutureFlags.Server.Features.Evaluation.OfrepEvaluateFlag;

/// <summary>
/// <c>POST /ofrep/v1/evaluate/flags/{key}</c> — the OpenFeature Remote Evaluation Protocol's
/// single-flag route, for a server-side provider evaluating one key against a per-request context.
///
/// <para>
/// Same credential rules as the bulk route: either kind of SDK key, because this answers with a
/// value rather than with definitions.
/// </para>
/// </summary>
public static class OfrepEvaluateFlagEndpoint
{
    public static IEndpointRouteBuilder MapOfrepEvaluateFlag(this IEndpointRouteBuilder endpoints)
    {
        // See the note on the bulk endpoint for why the return type is spelled out.
        endpoints.MapPost("/v1/evaluate/flags/{key}", async Task<IResult> (
            string key,
            HttpContext context,
            OfrepEvaluateFlagHandler handler,
            CancellationToken cancellationToken) =>
        {
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

            // A key that is not even shaped like one cannot name a flag, so it gets the same answer
            // as a key that names no flag — a caller cannot tell "malformed" from "absent" and does
            // not need to.
            var flagKey = FlagKey.Create(key);
            if (flagKey.IsFailure)
            {
                var missing = OfrepErrors.FlagNotFound(key);

                return Results.NotFound(new OfrepFlagFailure(key, missing.Code, missing.Message));
            }

            OfrepEvaluateFlagRequest? request;

            try
            {
                request = await context.Request.ReadFromJsonAsync<OfrepEvaluateFlagRequest>(cancellationToken);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new OfrepFlagFailure(
                    key,
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
                return Results.BadRequest(new OfrepFlagFailure(
                    key,
                    evaluationContext.Error.Code,
                    evaluationContext.Error.Message));
            }

            var result = await handler.HandleAsync(
                new OfrepEvaluateFlagQuery(environment.Value, flagKey.Value, evaluationContext.Value),
                cancellationToken);

            return result.Match(
                Results.Ok,
                // OFREP's own failure shape rather than ProblemDetails: the provider reads
                // errorCode to decide whether to serve the caller's default, and ProblemDetails
                // would put that field somewhere it does not look.
                error => Results.NotFound(new OfrepFlagFailure(key, error.Code, error.Message)));
        })
        .RequireAuthorization(AuthPolicies.SdkKey)
        .RequireCors(BrowserOrigins.PolicyName)
        .WithMetadata(new RequestSizeLimitAttribute(Ofrep.MaxRequestBytes))
        .WithName("OfrepEvaluateFlag")
        .WithSummary("One flag's value for one context, in the OpenFeature Remote Evaluation Protocol's shape.")
        .Produces<OfrepFlagResult>()
        .Produces<OfrepFlagFailure>(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .Produces<OfrepFlagFailure>(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
