using FutureFlags.Domain.Flags;
using FutureFlags.Domain.Shared;
using FutureFlags.Server.Api;

namespace FutureFlags.Server.Features.Flags.GetFlagHistory;

public static class GetFlagHistoryEndpoint
{
    public static IEndpointRouteBuilder MapGetFlagHistory(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/flags/{key}/history", async (
            string key,
            GetFlagHistoryHandler handler,
            CancellationToken cancellationToken) =>
        {
            var keyResult = FlagKey.Create(key);
            if (keyResult.IsFailure)
            {
                return keyResult.Error.ToProblem();
            }

            var result = await handler.HandleAsync(new GetFlagHistoryQuery(keyResult.Value), cancellationToken);

            return result.Match(
                response => Results.Ok(response),
                error => error.ToProblem());
        })
        .RequireAuthorization(AuthPolicies.SignedIn)
        .WithName("GetFlagHistory")
        .WithSummary("Reads a flag's full activity history — creation, edits, and toggles.")
        .Produces<GetFlagHistoryResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
