using System.Security.Claims;
using FutureFlags.Domain.Flags;
using FutureFlags.Domain.Shared;
using FutureFlags.Domain.Users;
using FutureFlags.Server.Api;

namespace FutureFlags.Server.Features.Flags.UpdateFlag;

public static class UpdateFlagEndpoint
{
    public static IEndpointRouteBuilder MapUpdateFlag(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/flags/{key}", async (
            string key,
            UpdateFlagRequest request,
            ClaimsPrincipal principal,
            UpdateFlagHandler handler,
            CancellationToken cancellationToken) =>
        {
            var keyResult = FlagKey.Create(key);
            if (keyResult.IsFailure)
            {
                return keyResult.Error.ToProblem();
            }

            var causedBy = principal.GetUserId().ToResult(UserErrors.NotProvisioned);
            if (causedBy.IsFailure)
            {
                return causedBy.Error.ToProblem();
            }

            var result = await handler.HandleAsync(
                new UpdateFlagCommand(keyResult.Value, request.Name, request.Description, causedBy.Value),
                cancellationToken);

            return result.Match(
                response => Results.Ok(response),
                error => error.ToProblem());
        })
        .RequireAuthorization(AuthPolicies.SignedIn)
        .WithName("UpdateFlag")
        .WithSummary("Updates a flag's name and description. The key cannot be changed.")
        .Produces<UpdateFlagResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
