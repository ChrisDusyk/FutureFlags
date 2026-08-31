using FutureFlags.Domain.Shared;

namespace FutureFlags.Domain.Users;

/// <summary>
/// Read-only by design: the mirror is written by a database trigger, never by the application.
/// </summary>
public interface IUserRepository
{
    Task<Option<User>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every user matching one of <paramref name="ids"/>, in one round trip rather than
    /// one per id — a caller resolving several distinct actors at once uses this instead of
    /// looping <see cref="GetByIdAsync"/>. Silently drops any id with no matching row.</summary>
    Task<IReadOnlyList<User>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
