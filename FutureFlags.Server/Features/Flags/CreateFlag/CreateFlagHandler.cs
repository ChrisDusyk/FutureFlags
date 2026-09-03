using FutureFlags.Domain.Flags;
using FutureFlags.Domain.Shared;

namespace FutureFlags.Server.Features.Flags.CreateFlag;

public sealed class CreateFlagHandler(IFeatureFlagRepository repository, TimeProvider timeProvider)
{
    public async Task<Result<CreateFlagResponse>> HandleAsync(
        CreateFlagCommand command,
        CancellationToken cancellationToken = default)
    {
        var flagResult = FeatureFlag.Create(
            command.Key,
            command.Name,
            command.Description,
            command.EnabledIn,
            timeProvider.GetUtcNow(),
            command.CausedBy,
            command.ValueType);

        if (flagResult.IsFailure)
            return Result.Failure<CreateFlagResponse>(flagResult.Error);

        var flag = flagResult.Value;

        // Answers the common case without a round trip to a failed insert. The unique index is
        // what actually settles a race, and SaveChangesAsync reports that as a failure too, so
        // both paths return the same Conflict.
        var existing = await repository.GetByKeyAsync(flag.Key, cancellationToken);
        if (existing.IsSome)
            return Result.Failure<CreateFlagResponse>(FlagErrors.DuplicateKey(flag.Key));

        await repository.AddAsync(flag, cancellationToken);

        var saveResult = await repository.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return Result.Failure<CreateFlagResponse>(saveResult.Error);

        return Result.Success(CreateFlagResponse.From(flag));
    }
}
