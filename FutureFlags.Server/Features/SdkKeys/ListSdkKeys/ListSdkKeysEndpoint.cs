using FutureFlags.Domain.Shared;
using FutureFlags.Server.Api;

namespace FutureFlags.Server.Features.SdkKeys.ListSdkKeys;

public static class ListSdkKeysEndpoint
{
    public static IEndpointRouteBuilder MapListSdkKeys(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/sdk-keys", async (
            ListSdkKeysHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(cancellationToken);

            return result.Match(
                response => Results.Ok(response),
                error => error.ToProblem());
        })
        .RequireAuthorization(AuthPolicies.Admin)
        .WithName("ListSdkKeys")
        .WithSummary("Lists every SDK key, revoked ones included.")
        .Produces<ListSdkKeysResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }
}
