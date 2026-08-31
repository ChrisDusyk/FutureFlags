using FutureFlags.Domain.Environments;
using FutureFlags.Domain.Shared;
using FutureFlags.Server.Api;

namespace FutureFlags.Server.Features.Flags.ListFlags;

public static class ListFlagsEndpoint
{
    public static IEndpointRouteBuilder MapListFlags(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/flags", async (
            string? environment,
            ListFlagsHandler handler,
            CancellationToken cancellationToken) =>
        {
            // EnvironmentKey.Create carries both the missing and the unrecognized case, so a
            // typo'd environment is a 400 rather than a list quietly answered for somewhere else.
            var environmentResult = EnvironmentKey.Create(environment);

            if (environmentResult.IsFailure)
            {
                return environmentResult.Error.ToProblem();
            }

            var result = await handler.HandleAsync(
                new ListFlagsQuery(environmentResult.Value),
                cancellationToken);

            return result.Match(
                response => Results.Ok(response),
                error => error.ToProblem());
        })
        .RequireAuthorization(AuthPolicies.SignedIn)
        .WithName("ListFlags")
        .WithSummary("Lists every flag with its state in one environment.")
        .Produces<ListFlagsResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        return endpoints;
    }
}
