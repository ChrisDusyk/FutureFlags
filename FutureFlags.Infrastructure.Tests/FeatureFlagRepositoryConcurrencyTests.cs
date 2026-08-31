using FutureFlags.Domain.Environments;
using FutureFlags.Domain.Flags;
using FutureFlags.Infrastructure.Persistence.Repositories;

namespace FutureFlags.Infrastructure.Tests;

/// <summary>
/// Proves the one piece of behavior a fake repository cannot: that a Postgres primary-key
/// violation on <c>(FlagId, SequenceNumber)</c> — two writers who both read a flag at the same
/// version and then both save — actually happens and actually translates to
/// <see cref="FlagErrors.ConcurrencyConflict"/>, not just that a handler forwards one when told to.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class FeatureFlagRepositoryConcurrencyTests(PostgresFixture postgres)
{
    private static readonly Guid CausedBy = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task SaveChangesAsync_WhenTwoWritersToggleTheSameFlagFromTheSameVersion_TheSecondShouldFailWithConcurrencyConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = FlagKey.Create($"concurrency-{Guid.NewGuid():N}").Value;
        var now = DateTimeOffset.UtcNow;

        await using (var seedContext = postgres.NewDbContext())
        {
            var seedRepository = new FeatureFlagRepository(seedContext);
            var flag = FeatureFlag.Create(key.Value, "Concurrency test", null, [], now, CausedBy).Value;

            await seedRepository.AddAsync(flag, cancellationToken);
            var seedResult = await seedRepository.SaveChangesAsync(cancellationToken);

            Assert.True(seedResult.IsSuccess);
        }

        // Two separate scopes, each reading the flag at its current (post-seed) version — mirrors
        // two concurrent requests that both load before either writes.
        await using var firstContext = postgres.NewDbContext();
        await using var secondContext = postgres.NewDbContext();

        var firstRepository = new FeatureFlagRepository(firstContext);
        var secondRepository = new FeatureFlagRepository(secondContext);

        var firstFlag = (await firstRepository.GetByKeyAsync(key, cancellationToken))
            .Match(flag => flag, () => throw new InvalidOperationException("Seed flag missing."));
        var secondFlag = (await secondRepository.GetByKeyAsync(key, cancellationToken))
            .Match(flag => flag, () => throw new InvalidOperationException("Seed flag missing."));

        firstFlag.SetEnabled(EnvironmentKey.Production, isEnabled: true, now.AddMinutes(1), CausedBy);
        secondFlag.SetEnabled(EnvironmentKey.Staging, isEnabled: true, now.AddMinutes(1), CausedBy);

        var firstResult = await firstRepository.SaveChangesAsync(cancellationToken);
        Assert.True(firstResult.IsSuccess);

        var secondResult = await secondRepository.SaveChangesAsync(cancellationToken);

        Assert.True(secondResult.IsFailure);
        Assert.Equal(FlagErrors.ConcurrencyConflict(key), secondResult.Error);
    }
}
