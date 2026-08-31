namespace FutureFlags.Domain.Users;

/// <summary>
/// A person who can sign in to the console, as the application sees them.
/// </summary>
/// <remarks>
/// This is a mirror, not a source. Better Auth owns the identity — credentials, sessions, and
/// the authoritative role all live in the <c>auth</c> schema — and a trigger on <c>auth.user</c>
/// keeps this copy in step. The application reads it so that a flag can be attributed to a
/// person without every query reaching across into a schema it does not own; nothing here
/// writes it, which is why there is no <c>Create</c> factory and no mutators.
/// </remarks>
public sealed class User
{
    public const int MaxEmailLength = 320;
    public const int MaxNameLength = 200;

    private User(
        Guid id,
        string email,
        string name,
        UserRole role,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        Email = email;
        Name = name;
        Role = role;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    // EF Core materialization only.
    private User()
    {
        Email = null!;
        Name = null!;
        Role = null!;
    }

    public Guid Id { get; private set; }
    public string Email { get; private set; }
    public string Name { get; private set; }
    public UserRole Role { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsAdmin => Role.IsAdmin;

    /// <summary>
    /// Rebuilds a user from a row the mirror trigger wrote. For persistence and tests only —
    /// the application has no business minting an identity, since Better Auth owns that.
    /// </summary>
    public static User FromPersisted(
        Guid id,
        string email,
        string name,
        UserRole role,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt) =>
        new(id, email, name, role, createdAt, updatedAt);
}
