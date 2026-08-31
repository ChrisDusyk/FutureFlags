using FutureFlags.Domain.Shared;
using FutureFlags.Server.Api;

namespace FutureFlags.Server.Features.SdkKeys.RevokeSdkKey;

public static class RevokeSdkKeyEndpoint
{
    public static IEndpointRouteBuilder MapRevokeSdkKey(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDelete("/sdk-keys/{id:guid}", async (
            Guid id,
            RevokeSdkKeyHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, cancellationToken);

            return result.Match<IResult>(
                Results.NoContent,
                error => error.ToProblem());
        })
        .RequireAuthorization(AuthPolicies.Admin)
        .WithName("RevokeSdkKey")
        .WithSummary("Revokes an SDK key. The row stays, so a key that stopped working can be told from one that never existed.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }
}
