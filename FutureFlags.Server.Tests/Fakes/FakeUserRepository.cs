using FutureFlags.Domain.Shared;
using FutureFlags.Domain.Users;

namespace FutureFlags.Server.Tests.Fakes;

/// <summary>
/// In-memory stand-in for the EF repository. Read-only, like the real one — the mirror is
/// written by a database trigger, so there is no Add or Save to fake.
/// <para>
/// Lives outside Features/ because it stands in for a Domain interface more than one flag slice
/// depends on — copying it into each slice would make them drift.
/// </para>
/// </summary>
internal sealed class FakeUserRepository : IUserRepository
{
    private readonly Dictionary<Guid, User> _users = [];

    public void Seed(User user) => _users[user.Id] = user;

    public Task<Option<User>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.TryGetValue(id, out var user)
            ? Option<User>.Some(user)
            : Option<User>.None);

    public Task<IReadOnlyList<User>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<User>>([.. ids.Where(_users.ContainsKey).Select(id => _users[id])]);
}
