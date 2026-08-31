using System.Security.Claims;
using FutureFlags.Domain.Environments;
using FutureFlags.Domain.Shared;
using FutureFlags.Domain.Users;
using FutureFlags.Server.Api;

namespace FutureFlags.Server.Features.Flags.CreateFlag;

public static class CreateFlagEndpoint
{
    public static IEndpointRouteBuilder MapCreateFlag(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/flags", async (
            CreateFlagRequest request,
            ClaimsPrincipal principal,
            CreateFlagHandler handler,
            CancellationToken cancellationToken) =>
        {
            // Resolve the environment names before the domain sees them, so an unrecognized one is
            // a 400 about that name rather than a flag quietly created on in nowhere.
            var enabledIn = new List<EnvironmentKey>();

            foreach (var name in request.EnabledIn ?? [])
            {
                var environmentResult = EnvironmentKey.Create(name);

                if (environmentResult.IsFailure)
                {
                    return environmentResult.Error.ToProblem();
                }

                enabledIn.Add(environmentResult.Value);
            }

            var causedBy = principal.GetUserId().ToResult(UserErrors.NotProvisioned);

            if (causedBy.IsFailure)
            {
                return causedBy.Error.ToProblem();
            }

            var command = new CreateFlagCommand(request.Key, request.Name, request.Description, enabledIn, causedBy.Value);

            var result = await handler.HandleAsync(command, cancellationToken);

            return result.Match(
                response => Results.Created($"/api/flags/{response.Key}", response),
                error => error.ToProblem());
        })
        .RequireAuthorization(AuthPolicies.SignedIn)
        .WithName("CreateFlag")
        .WithSummary("Creates a feature flag.")
        .Produces<CreateFlagResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }
}
