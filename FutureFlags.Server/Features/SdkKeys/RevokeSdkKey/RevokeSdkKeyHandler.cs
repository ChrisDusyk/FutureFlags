using FutureFlags.Domain.SdkKeys;
using FutureFlags.Domain.Shared;

namespace FutureFlags.Server.Features.SdkKeys.RevokeSdkKey;

public sealed class RevokeSdkKeyHandler(ISdkKeyRepository repository, TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var found = await repository.GetByIdAsync(id, cancellationToken);

        var key = found.ToResult(SdkKeyErrors.NotFound(id));

        if (key.IsFailure)
        {
            return Result.Failure(key.Error);
        }

        var revoked = key.Value.Revoke(timeProvider.GetUtcNow());

        if (revoked.IsFailure)
        {
            return revoked;
        }

        return await repository.SaveChangesAsync(cancellationToken);
    }
}
