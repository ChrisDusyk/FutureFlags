using System.Security.Claims;
using FeatureFlags.Domain.Segments;
using FeatureFlags.Domain.Shared;
using FeatureFlags.Domain.Users;
using FeatureFlags.Server.Api;

namespace FeatureFlags.Server.Features.Segments.CreateSegment;

public static class CreateSegmentEndpoint
{
    public static IEndpointRouteBuilder MapCreateSegment(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/segments", async (
            CreateSegmentRequest request,
            ClaimsPrincipal principal,
            CreateSegmentHandler handler,
            CancellationToken cancellationToken) =>
        {
            var conditions = new List<SegmentCondition>();

            foreach (var condition in request.Definition?.Conditions ?? [])
            {
                var conditionResult = SegmentCondition.Create(condition.Attribute, condition.Operator, condition.Values);
                if (conditionResult.IsFailure)
                {
                    return conditionResult.Error.ToProblem();
                }

                conditions.Add(conditionResult.Value);
            }

            var definitionResult = SegmentDefinition.Create(
                request.Definition?.IncludedKeys, request.Definition?.ExcludedKeys, conditions);

            if (definitionResult.IsFailure)
            {
                return definitionResult.Error.ToProblem();
            }

            var causedBy = principal.GetUserId().ToResult(UserErrors.NotProvisioned);
            if (causedBy.IsFailure)
            {
                return causedBy.Error.ToProblem();
            }

            var command = new CreateSegmentCommand(
                request.Key, request.Name, request.Description, definitionResult.Value, causedBy.Value);

            var result = await handler.HandleAsync(command, cancellationToken);

            return result.Match(
                response => Results.Created($"/api/segments/{response.Key}", response),
                error => error.ToProblem());
        })
        .RequireAuthorization(AuthPolicies.SignedIn)
        .WithName("CreateSegment")
        .WithSummary("Creates a segment.")
        .Produces<CreateSegmentResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }
}
