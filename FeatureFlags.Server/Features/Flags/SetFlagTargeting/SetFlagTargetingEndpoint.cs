using System.Security.Claims;
using FeatureFlags.Domain.Environments;
using FeatureFlags.Domain.Flags;
using FeatureFlags.Domain.Segments;
using FeatureFlags.Domain.Shared;
using FeatureFlags.Domain.Users;
using FeatureFlags.Server.Api;

namespace FeatureFlags.Server.Features.Flags.SetFlagTargeting;

public static class SetFlagTargetingEndpoint
{
    public static IEndpointRouteBuilder MapSetFlagTargeting(this IEndpointRouteBuilder endpoints)
    {
        // PUT on a subresource, the same shape as /flags/{key}/state and for the same reason: the
        // caller sends the set it wants, so a retry after a dropped response sets the same thing.
        endpoints.MapPut("/flags/{key}/targeting", async (
            string key,
            SetFlagTargetingRequest request,
            ClaimsPrincipal principal,
            SetFlagTargetingHandler handler,
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

            var segments = new List<SegmentKey>();

            foreach (var segment in request.Segments ?? [])
            {
                var segmentResult = SegmentKey.Create(segment);
                if (segmentResult.IsFailure)
                {
                    return segmentResult.Error.ToProblem();
                }

                segments.Add(segmentResult.Value);
            }

            var causedBy = principal.GetUserId().ToResult(UserErrors.NotProvisioned);
            if (causedBy.IsFailure)
            {
                return causedBy.Error.ToProblem();
            }

            var result = await handler.HandleAsync(
                new SetFlagTargetingCommand(keyResult.Value, environmentResult.Value, segments, causedBy.Value),
                cancellationToken);

            return result.Match(
                response => Results.Ok(response),
                error => error.ToProblem());
        })
        .RequireAuthorization(AuthPolicies.SignedIn)
        .WithName("SetFlagTargeting")
        .WithSummary("Replaces the segments a flag reaches in one environment.")
        .Produces<SetFlagTargetingResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        // Another writer racing this one on the same flag's event stream — SaveChangesAsync
        // translates that to FlagErrors.ConcurrencyConflict. ToggleFlag and UpdateFlag can fail
        // the same way and do not declare it; that gap predates this slice and is not fixed here.
        .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }
}
