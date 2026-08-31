using FutureFlags.Domain.Flags;
using FutureFlags.Domain.Shared;

namespace FutureFlags.Server.Features.Flags.ToggleFlag;

public sealed class ToggleFlagHandler(IFeatureFlagRepository repository, TimeProvider timeProvider)
{
    public async Task<Result<ToggleFlagResponse>> HandleAsync(
        ToggleFlagCommand command,
        CancellationToken cancellationToken = default)
    {
        var flagResult = (await repository.GetByKeyAsync(command.Key, cancellationToken))
            .ToResult(FlagErrors.NotFound(command.Key));

        if (flagResult.IsFailure)
            return Result.Failure<ToggleFlagResponse>(flagResult.Error);

        var flag = flagResult.Value;

        var setResult = flag.SetEnabled(command.Environment, command.IsEnabled, timeProvider.GetUtcNow(), command.CausedBy);
        if (setResult.IsFailure)
            return Result.Failure<ToggleFlagResponse>(setResult.Error);

        // Saving even when the state did not move keeps this path free of a "did anything change?"
        // branch. EF sends nothing for an unmodified entity, so the no-op costs a round trip at worst.
        var saveResult = await repository.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return Result.Failure<ToggleFlagResponse>(saveResult.Error);

        return Result.Success(ToggleFlagResponse.From(flag, command.Environment));
    }
}
