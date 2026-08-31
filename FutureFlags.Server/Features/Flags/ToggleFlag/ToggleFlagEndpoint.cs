using System.Security.Claims;
using FutureFlags.Domain.Environments;
using FutureFlags.Domain.Flags;
using FutureFlags.Domain.Shared;
using FutureFlags.Domain.Users;
using FutureFlags.Server.Api;

namespace FutureFlags.Server.Features.Flags.ToggleFlag;

public static class ToggleFlagEndpoint
{
    public static IEndpointRouteBuilder MapToggleFlag(this IEndpointRouteBuilder endpoints)
    {
        // PUT on the state subresource rather than POST /toggle: the caller sends the state it
        // wants, so a retry after a dropped response sets the same thing instead of flipping it back.
        endpoints.MapPut("/flags/{key}/state", async (
            string key,
            ToggleFlagRequest request,
            ClaimsPrincipal principal,
            ToggleFlagHandler handler,
            CancellationToken cancellationToken) =>
        {
            var keyResult = FlagKey.Create(key);
            if (keyResult.IsFailure)
            {
                return keyResult.Error.ToProblem();
            }

            var environmentResult = EnvironmentKey.Create(request.Environment);
            if (environmentResult.IsFailure)
            {
                return environmentResult.Error.ToProblem();
            }

            var causedBy = principal.GetUserId().ToResult(UserErrors.NotProvisioned);
            if (causedBy.IsFailure)
            {
                return causedBy.Error.ToProblem();
            }

            var result = await handler.HandleAsync(
                new ToggleFlagCommand(keyResult.Value, environmentResult.Value, request.IsEnabled, causedBy.Value),
                cancellationToken);

            return result.Match(
                response => Results.Ok(response),
                error => error.ToProblem());
        })
        .RequireAuthorization(AuthPolicies.SignedIn)
        .WithName("ToggleFlag")
        .WithSummary("Turns a flag on or off in one environment.")
        .Produces<ToggleFlagResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
