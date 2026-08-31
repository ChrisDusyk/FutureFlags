using FutureFlags.Domain.Shared;

namespace FutureFlags.Domain.Users;

public static class UserErrors
{
    public static Error RoleRequired => Error.Validation(
        "User.Role.Required",
        "A user role is required.");

    public static Error RoleUnrecognized(string value) => Error.Validation(
        "User.Role.Unrecognized",
        $"'{value}' is not a role this application recognizes.");

    public static Error NotFound(Guid id) => Error.NotFound(
        "User.NotFound",
        $"No user with the id '{id}' exists.");

    /// <summary>
    /// The caller holds a valid token for an identity that has no row in the application's
    /// mirror — which means the trigger on <c>auth.user</c> has not run for it.
    /// </summary>
    public static Error NotProvisioned => Error.Unauthorized(
        "User.NotProvisioned",
        "This account has not finished being set up. Sign out and back in.");
}
