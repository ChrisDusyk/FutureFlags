using FutureFlags.Domain.Shared;
using FutureFlags.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace FutureFlags.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(AppDbContext dbContext) : IUserRepository
{
    public async Task<Option<User>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return user.ToOption();
    }

    public async Task<IReadOnlyList<User>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
            return [];

        return await dbContext.Users
            .AsNoTracking()
            .Where(candidate => ids.Contains(candidate.Id))
            .ToListAsync(cancellationToken);
    }
}
