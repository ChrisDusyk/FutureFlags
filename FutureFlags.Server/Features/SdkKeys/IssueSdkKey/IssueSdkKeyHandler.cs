using FutureFlags.Domain.SdkKeys;
using FutureFlags.Domain.Shared;

namespace FutureFlags.Server.Features.SdkKeys.IssueSdkKey;

public sealed class IssueSdkKeyHandler(ISdkKeyRepository repository, TimeProvider timeProvider)
{
    public async Task<Result<IssueSdkKeyResponse>> HandleAsync(
        IssueSdkKeyCommand command,
        CancellationToken cancellationToken = default)
    {
        var issued = SdkKey.Issue(
            command.Name,
            command.Kind,
            command.Environment,
            command.IssuedBy,
            timeProvider.GetUtcNow());

        if (issued.IsFailure)
        {
            return Result.Failure<IssueSdkKeyResponse>(issued.Error);
        }

        await repository.AddAsync(issued.Value.Key, cancellationToken);

        var saved = await repository.SaveChangesAsync(cancellationToken);

        return saved.IsFailure
            ? Result.Failure<IssueSdkKeyResponse>(saved.Error)
            // The token is carried out of here exactly once. Nothing logs it, nothing stores it,
            // and no later read can recover it — only the hash survives.
            : Result.Success(IssueSdkKeyResponse.From(issued.Value));
    }
}
