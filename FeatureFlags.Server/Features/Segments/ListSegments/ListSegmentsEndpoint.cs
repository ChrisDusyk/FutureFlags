using FeatureFlags.Domain.Shared;
using FeatureFlags.Server.Api;

namespace FeatureFlags.Server.Features.Segments.ListSegments;

public static class ListSegmentsEndpoint
{
    public static IEndpointRouteBuilder MapListSegments(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/segments", async (
            ListSegmentsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(cancellationToken);

            return result.Match(
                response => Results.Ok(response),
                error => error.ToProblem());
        })
        .RequireAuthorization(AuthPolicies.SignedIn)
        .WithName("ListSegments")
        .WithSummary("Every segment, without its definition.")
        .Produces<ListSegmentsResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        return endpoints;
    }
}
