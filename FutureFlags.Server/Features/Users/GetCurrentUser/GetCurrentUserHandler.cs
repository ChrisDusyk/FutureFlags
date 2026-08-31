using FutureFlags.Domain.Shared;
using FutureFlags.Domain.Users;

namespace FutureFlags.Server.Features.Users.GetCurrentUser;

public sealed class GetCurrentUserHandler(IUserRepository users)
{
    public async Task<Result<GetCurrentUserResponse>> HandleAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);

        // A valid token for an identity with no mirrored row means the trigger on auth.user has
        // not run for it — the account exists to Better Auth but not yet to this application.
        return user
            .ToResult(UserErrors.NotProvisioned)
            .Map(found => new GetCurrentUserResponse(
                found.Id,
                found.Email,
                found.Name,
                found.Role.Value,
                found.IsAdmin));
    }
}
