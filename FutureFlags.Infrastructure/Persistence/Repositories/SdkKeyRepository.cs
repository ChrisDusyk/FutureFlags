using FutureFlags.Domain.SdkKeys;
using FutureFlags.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace FutureFlags.Infrastructure.Persistence.Repositories;

internal sealed class SdkKeyRepository(AppDbContext dbContext) : ISdkKeyRepository
{
    public async Task<IReadOnlyList<SdkKey>> ListAsync(CancellationToken cancellationToken = default) =>
        // Ids are UUIDv7, so descending by id is descending by when it was issued.
        await dbContext.SdkKeys
            .OrderByDescending(key => key.Id)
            .ToListAsync(cancellationToken);

    public async Task<Option<SdkKey>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var key = await dbContext.SdkKeys
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return key.ToOption();
    }

    public async Task<Option<SdkKey>> GetBySelectorAsync(
        string selector,
        CancellationToken cancellationToken = default)
    {
        var key = await dbContext.SdkKeys
            .FirstOrDefaultAsync(candidate => candidate.Selector == selector, cancellationToken);

        return key.ToOption();
    }

    public async Task AddAsync(SdkKey key, CancellationToken cancellationToken = default) =>
        await dbContext.SdkKeys.AddAsync(key, cancellationToken);

    /// <summary>
    /// No conflict to translate here, unlike flags: a selector collision would mean two keys drew
    /// the same 64 random bits, which is not a race worth writing code for. If it ever happened the
    /// unique index would still stop it, as an exception rather than a Result — which is the right
    /// shape for something that should never occur.
    /// </summary>
    public async Task<Result> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
