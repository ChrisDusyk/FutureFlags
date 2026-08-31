using FutureFlags.Domain.Flags;
using FutureFlags.Domain.Shared;
using FutureFlags.Server.Api;

namespace FutureFlags.Server.Features.Flags.GetFlag;

public static class GetFlagEndpoint
{
    public static IEndpointRouteBuilder MapGetFlag(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/flags/{key}", async (
            string key,
            GetFlagHandler handler,
            CancellationToken cancellationToken) =>
        {
            var keyResult = FlagKey.Create(key);
            if (keyResult.IsFailure)
            {
                return keyResult.Error.ToProblem();
            }

            var result = await handler.HandleAsync(new GetFlagQuery(keyResult.Value), cancellationToken);

            return result.Match(
                response => Results.Ok(response),
                error => error.ToProblem());
        })
        .RequireAuthorization(AuthPolicies.SignedIn)
        .WithName("GetFlag")
        .WithSummary("Reads one flag's current details and state.")
        .Produces<GetFlagResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
