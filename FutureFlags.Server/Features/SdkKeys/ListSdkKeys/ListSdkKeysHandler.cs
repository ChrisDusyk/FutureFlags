using FutureFlags.Domain.SdkKeys;
using FutureFlags.Domain.Shared;

namespace FutureFlags.Server.Features.SdkKeys.ListSdkKeys;

public sealed class ListSdkKeysHandler(ISdkKeyRepository repository)
{
    public async Task<Result<ListSdkKeysResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var keys = await repository.ListAsync(cancellationToken);

        var summaries = keys
            .Select(SdkKeySummary.From)
            .ToList();

        return Result.Success(new ListSdkKeysResponse(summaries));
    }
}
