using FutureFlags.Domain.Flags;
using FutureFlags.Domain.Shared;

namespace FutureFlags.Server.Features.Flags.UpdateFlag;

public sealed class UpdateFlagHandler(IFeatureFlagRepository repository, TimeProvider timeProvider)
{
    public async Task<Result<UpdateFlagResponse>> HandleAsync(
        UpdateFlagCommand command,
        CancellationToken cancellationToken = default)
    {
        var flagResult = (await repository.GetByKeyAsync(command.Key, cancellationToken))
            .ToResult(FlagErrors.NotFound(command.Key));

        if (flagResult.IsFailure)
            return Result.Failure<UpdateFlagResponse>(flagResult.Error);

        var flag = flagResult.Value;

        var updateResult = flag.UpdateDetails(command.Name, command.Description, timeProvider.GetUtcNow(), command.CausedBy);
        if (updateResult.IsFailure)
            return Result.Failure<UpdateFlagResponse>(updateResult.Error);

        var saveResult = await repository.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return Result.Failure<UpdateFlagResponse>(saveResult.Error);

        return Result.Success(UpdateFlagResponse.From(flag));
    }
}
