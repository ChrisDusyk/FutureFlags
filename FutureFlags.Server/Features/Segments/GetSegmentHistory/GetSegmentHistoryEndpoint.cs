using FutureFlags.Domain.Segments;
using FutureFlags.Domain.Shared;
using FutureFlags.Server.Api;

namespace FutureFlags.Server.Features.Segments.GetSegmentHistory;

public static class GetSegmentHistoryEndpoint
{
    public static IEndpointRouteBuilder MapGetSegmentHistory(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/segments/{key}/history", async (
            string key,
            GetSegmentHistoryHandler handler,
            CancellationToken cancellationToken) =>
        {
            var keyResult = SegmentKey.Create(key);
            if (keyResult.IsFailure)
            {
                return keyResult.Error.ToProblem();
            }

            var result = await handler.HandleAsync(new GetSegmentHistoryQuery(keyResult.Value), cancellationToken);

            return result.Match(
                response => Results.Ok(response),
                error => error.ToProblem());
        })
        .RequireAuthorization(AuthPolicies.SignedIn)
        .WithName("GetSegmentHistory")
        .WithSummary("One segment's activity, newest first.")
        .Produces<GetSegmentHistoryResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
