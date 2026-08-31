namespace FutureFlags.Server.Features.Users.GetCurrentUser;

/// <summary>
/// Who the caller is, as the application knows them. The console reads this rather than trusting
/// its own decoded token, so the role it renders against is the one the API will actually enforce.
/// </summary>
public sealed record GetCurrentUserResponse(
    Guid Id,
    string Email,
    string Name,
    string Role,
    bool IsAdmin);
