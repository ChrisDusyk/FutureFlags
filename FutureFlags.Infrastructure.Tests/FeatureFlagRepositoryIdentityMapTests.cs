using FutureFlags.Domain.Environments;
using FutureFlags.Domain.Flags;
using FutureFlags.Infrastructure.Persistence.Repositories;

namespace FutureFlags.Infrastructure.Tests;

/// <summary>
/// Proves a repository instance hands back the same tracked <see cref="FeatureFlag"/> for a
/// repeat read within its own scope, rather than rehydrating a second instance of the same row.
/// Two instances mutated independently would each think they own the next event-stream sequence
/// number, and saving both would append overlapping ranges — a concurrency conflict this
/// repository caused itself, not a real race with another writer.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class FeatureFlagRepositoryIdentityMapTests(PostgresFixture postgres)
{
    private static readonly Guid CausedBy = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task GetByKeyAsync_CalledTwiceInTheSameScope_ShouldReturnTheSameInstance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = FlagKey.Create($"identity-map-{Guid.NewGuid():N}").Value;
        var now = DateTimeOffset.UtcNow;

        await using var dbContext = postgres.NewDbContext();
        var repository = new FeatureFlagRepository(dbContext);

        var flag = FeatureFlag.Create(key.Value, "Identity map test", null, [], now, CausedBy).Value;
        await repository.AddAsync(flag, cancellationToken);
        Assert.True((await repository.SaveChangesAsync(cancellationToken)).IsSuccess);

        var first = (await repository.GetByKeyAsync(key, cancellationToken))
            .Match(f => f, () => throw new InvalidOperationException("Seed flag missing."));
        var second = (await repository.GetByKeyAsync(key, cancellationToken))
            .Match(f => f, () => throw new InvalidOperationException("Seed flag missing."));

        Assert.Same(first, second);
    }

    [Fact]
    public async Task SaveChangesAsync_AfterTwoReadsOfTheSameFlag_ShouldAppendOnlyOneEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = FlagKey.Create($"identity-map-{Guid.NewGuid():N}").Value;
        var now = DateTimeOffset.UtcNow;

        await using var dbContext = postgres.NewDbContext();
        var repository = new FeatureFlagRepository(dbContext);

        var seeded = FeatureFlag.Create(key.Value, "Identity map test", null, [], now, CausedBy).Value;
        await repository.AddAsync(seeded, cancellationToken);
        Assert.True((await repository.SaveChangesAsync(cancellationToken)).IsSuccess);
        var versionAfterSeed = seeded.Version;

        // Two reads of the same flag within this scope, mirroring a handler that (for whatever
        // reason) looks it up more than once before mutating and saving.
        await repository.GetByKeyAsync(key, cancellationToken);
        var flag = (await repository.GetByKeyAsync(key, cancellationToken))
            .Match(f => f, () => throw new InvalidOperationException("Seed flag missing."));

        flag.SetEnabled(EnvironmentKey.Production, isEnabled: true, now.AddMinutes(1), CausedBy);

        var result = await repository.SaveChangesAsync(cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(versionAfterSeed + 1, flag.Version);
    }
}
