using System.Security.Claims;
using FutureFlags.Domain.Segments;
using FutureFlags.Domain.Shared;
using FutureFlags.Domain.Users;
using FutureFlags.Server.Api;

namespace FutureFlags.Server.Features.Segments.DeleteSegment;

public static class DeleteSegmentEndpoint
{
    public static IEndpointRouteBuilder MapDeleteSegment(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDelete("/segments/{key}", async (
            string key,
            ClaimsPrincipal principal,
            DeleteSegmentHandler handler,
            CancellationToken cancellationToken) =>
        {
            var keyResult = SegmentKey.Create(key);
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
                new DeleteSegmentCommand(keyResult.Value, causedBy.Value), cancellationToken);

            return result.Match<IResult>(Results.NoContent, error => error.ToProblem());
        })
        .RequireAuthorization(AuthPolicies.SignedIn)
        .WithName("DeleteSegment")
        .WithSummary("Retires a segment, unless a flag still targets it.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }
}
