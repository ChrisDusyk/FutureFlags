using FutureFlags.Domain.Shared;

namespace FutureFlags.Domain.Users;

/// <summary>
/// What a signed-in person is allowed to do. There are exactly two: <see cref="User"/> is the
/// ordinary console experience, and <see cref="Admin"/> adds managing the organization's members.
/// </summary>
/// <remarks>
/// The stored values match the strings Better Auth's admin plugin writes to <c>auth.user.role</c>,
/// because that column is the source this mirrors.
/// </remarks>
public sealed record UserRole
{
    public const int MaxLength = 20;

    public static readonly UserRole User = new("user");
    public static readonly UserRole Admin = new("admin");

    private UserRole(string value) => Value = value;

    public string Value { get; }

    public bool IsAdmin => this == Admin;

    public static IReadOnlyList<UserRole> All { get; } = [User, Admin];

    public static Result<UserRole> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<UserRole>(UserErrors.RoleRequired);

        var normalized = value.Trim().ToLowerInvariant();
        var role = All.FirstOrDefault(candidate => candidate.Value == normalized);

        return role is null
            ? Result.Failure<UserRole>(UserErrors.RoleUnrecognized(value))
            : Result.Success(role);
    }

    /// <summary>
    /// Rehydrates a role that has already been validated on its way into storage.
    /// For persistence use only — this deliberately bypasses <see cref="Create"/>.
    /// </summary>
    public static UserRole FromPersisted(string value) =>
        All.FirstOrDefault(candidate => candidate.Value == value)
        ?? throw new InvalidOperationException($"'{value}' is not a role this application recognizes.");

    public override string ToString() => Value;
}
