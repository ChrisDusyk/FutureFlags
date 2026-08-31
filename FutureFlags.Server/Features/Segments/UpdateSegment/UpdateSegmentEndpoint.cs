using System.Security.Claims;
using FutureFlags.Domain.Segments;
using FutureFlags.Domain.Shared;
using FutureFlags.Domain.Users;
using FutureFlags.Server.Api;

namespace FutureFlags.Server.Features.Segments.UpdateSegment;

public static class UpdateSegmentEndpoint
{
    public static IEndpointRouteBuilder MapUpdateSegment(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/segments/{key}", async (
            string key,
            UpdateSegmentRequest request,
            ClaimsPrincipal principal,
            UpdateSegmentHandler handler,
            CancellationToken cancellationToken) =>
        {
            var keyResult = SegmentKey.Create(key);
            if (keyResult.IsFailure)
            {
                return keyResult.Error.ToProblem();
            }

            // Required, not defaulted: an update replaces the definition wholesale, and an omitted
            // one would otherwise silently become SegmentDefinition.Empty and clear whatever the
            // segment was matching.
            if (request.Definition is null)
            {
                return SegmentErrors.DefinitionRequired.ToProblem();
            }

            var conditions = new List<SegmentCondition>();

            foreach (var condition in request.Definition.Conditions ?? [])
            {
                var conditionResult = SegmentCondition.Create(condition.Attribute, condition.Operator, condition.Values);
                if (conditionResult.IsFailure)
                {
                    return conditionResult.Error.ToProblem();
                }

                conditions.Add(conditionResult.Value);
            }

            var definitionResult = SegmentDefinition.Create(
                request.Definition.IncludedKeys, request.Definition.ExcludedKeys, conditions);

            if (definitionResult.IsFailure)
            {
                return definitionResult.Error.ToProblem();
            }

            var causedBy = principal.GetUserId().ToResult(UserErrors.NotProvisioned);
            if (causedBy.IsFailure)
            {
                return causedBy.Error.ToProblem();
            }

            var command = new UpdateSegmentCommand(
                keyResult.Value, request.Name, request.Description, definitionResult.Value, causedBy.Value);

            var result = await handler.HandleAsync(command, cancellationToken);

            return result.Match(
                response => Results.Ok(response),
                error => error.ToProblem());
        })
        .RequireAuthorization(AuthPolicies.SignedIn)
        .WithName("UpdateSegment")
        .WithSummary("Replaces a segment's details and definition.")
        .Produces<UpdateSegmentResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }
}
