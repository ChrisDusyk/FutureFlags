using FutureFlags.Domain.SdkKeys;
using FutureFlags.Domain.Shared;
using FutureFlags.Evaluation;
using FutureFlags.Server.Api;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace FutureFlags.Server.Features.Evaluation.EvaluateForContext;

public static class EvaluateForContextEndpoint
{
    /// <summary>
    /// Caps on the one route where an authenticated caller decides how much work the server does.
    /// Generous enough that no honest context comes near them, small enough that a dishonest one
    /// cannot turn a bundled publishable key into a way to spend somebody's CPU.
    /// </summary>
    private const int MaxAttributes = 64;
    private const int MaxAttributeNameLength = 100;
    private const int MaxContextKeyLength = 256;
    private const int MaxRequestBytes = 16 * 1024;

    public static IEndpointRouteBuilder MapEvaluateForContext(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/evaluation", async (
            HttpContext context,
            EvaluateForContextHandler handler,
            CancellationToken cancellationToken) =>
        {
            // Before anything is read — and this time that is literally true rather than
            // aspirational. A minimal API endpoint binds a complex-type body parameter before its
            // delegate runs at all, so taking EvaluateForContextRequest as a parameter here would
            // have the framework deserialize the body ahead of these checks: a secret key from a
            // browser would still pay for the JSON parse before being refused. Taking HttpContext
            // instead and reading the body by hand, after the checks below, is what makes the
            // refusal actually free.
            var credential = BrowserCredentialRule.Check(context);
            if (credential.IsFailure)
            {
                return credential.Error.ToProblem();
            }

            var environment = context.User.GetSdkKeyEnvironment()
                .ToResult(SdkKeyErrors.TokenMalformed);

            if (environment.IsFailure)
            {
                return environment.Error.ToProblem();
            }

            EvaluateForContextRequest? request;

            try
            {
                request = await context.Request.ReadFromJsonAsync<EvaluateForContextRequest>(cancellationToken);
            }
            // Malformed JSON: the bytes we got don't parse.
            catch (JsonException)
            {
                return EvaluationErrors.MalformedBody.ToProblem();
            }
            // Kestrel enforces the size cap by throwing while the body is read, from wherever that
            // read happens to occur. RequestDelegateFactory's auto-generated binding for a bound
            // parameter catches this and answers with the exception's own status code — reading
            // the body by hand gets none of that for free, and without this catch a request over
            // MaxRequestBytes would surface as an unhandled 500 instead of the 413 it actually is.
            catch (BadHttpRequestException exception)
            {
                return Results.StatusCode(exception.StatusCode);
            }

            var evaluationContext = Bind(request?.Context);
            if (evaluationContext.IsFailure)
            {
                return evaluationContext.Error.ToProblem();
            }

            var result = await handler.HandleAsync(
                new EvaluateForContextQuery(environment.Value, evaluationContext.Value),
                cancellationToken);

            return result.Match(
                response => Results.Ok(response),
                error => error.ToProblem());
        })
        .RequireAuthorization(AuthPolicies.SdkKey)
        .RequireCors(BrowserOrigins.PolicyName)
        // Kestrel refuses a larger body before the handler is reached, so an oversized context
        // never becomes work this process does.
        .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
        // Deprecated, not removed: this is a documented compatibility surface and SDK versions in
        // the wild are not upgraded in step with the server. Behaviour is unchanged.
        .MarkDeprecated("Deprecated in favour of POST /ofrep/v1/evaluate/flags, which takes the same context in OpenFeature's shape. Still supported.")
        .WithName("EvaluateFlagsForContext")
        .WithSummary("Every flag's state for one person, in the environment the presented SDK key is scoped to.")
        .Produces<EvaluateForContextResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    internal static Result<FlagContext> Bind(EvaluateForContextContextRequest? request)
    {
        if (request is null)
            return Result.Success(FlagContext.Empty);

        if (request.Key is { Length: > MaxContextKeyLength })
            return Result.Failure<FlagContext>(EvaluationErrors.ContextTooLarge);

        var raw = request.Attributes ?? new Dictionary<string, AttributeValue>();
        var attributes = new Dictionary<string, AttributeValue>(StringComparer.Ordinal);

        foreach (var attribute in raw)
        {
            // Null is not a fourth kind of value, it is an absent one — the same reading
            // FlagContext's own normalisation, SegmentCondition.Create, and the Node client's
            // normalizeContext all give it. Dropped here rather than rejected, and not counted
            // against the cap below: it will never reach evaluation either way, so a context
            // carrying a few unset traits alongside real ones should not be penalised for
            // attributes this route already treats as absent.
            if (attribute.Value is null)
                continue;

            if (attributes.Count >= MaxAttributes)
                return Result.Failure<FlagContext>(EvaluationErrors.ContextTooLarge);

            if (attribute.Key.Length > MaxAttributeNameLength)
                return Result.Failure<FlagContext>(EvaluationErrors.ContextTooLarge);

            // A value no engine could agree on — an over-long string, a number past 2^53 — is
            // refused rather than compared. It would never match anything anyway, and saying so is
            // more use than silently answering false to everything.
            if (!attribute.Value.IsRepresentable)
                return Result.Failure<FlagContext>(EvaluationErrors.AttributeNotRepresentable(attribute.Key));

            attributes[attribute.Key] = attribute.Value;
        }

        return Result.Success(new FlagContext(request.Key, attributes));
    }
}
