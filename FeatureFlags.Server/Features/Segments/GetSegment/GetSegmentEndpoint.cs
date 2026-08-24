using FeatureFlags.Domain.Segments;
using FeatureFlags.Domain.Shared;
using FeatureFlags.Server.Api;

namespace FeatureFlags.Server.Features.Segments.GetSegment;

public static class GetSegmentEndpoint
{
    public static IEndpointRouteBuilder MapGetSegment(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/segments/{key}", async (
            string key,
            GetSegmentHandler handler,
            CancellationToken cancellationToken) =>
        {
            var keyResult = SegmentKey.Create(key);
            if (keyResult.IsFailure)
            {
                return keyResult.Error.ToProblem();
            }

            var result = await handler.HandleAsync(new GetSegmentQuery(keyResult.Value), cancellationToken);

            return result.Match(
                response => Results.Ok(response),
                error => error.ToProblem());
        })
        .RequireAuthorization(AuthPolicies.SignedIn)
        .WithName("GetSegment")
        .WithSummary("One segment, its definition, and the flags that target it.")
        .Produces<GetSegmentResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
