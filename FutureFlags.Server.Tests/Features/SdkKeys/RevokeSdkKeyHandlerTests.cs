using FutureFlags.Domain.Environments;
using FutureFlags.Domain.SdkKeys;
using FutureFlags.Server.Features.SdkKeys.RevokeSdkKey;
using FutureFlags.Server.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace FutureFlags.Server.Tests.Features.SdkKeys;

public class RevokeSdkKeyHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.CreateVersion7();

    private readonly FakeSdkKeyRepository _repository = new();
    private readonly FakeTimeProvider _timeProvider = new(Now);

    private RevokeSdkKeyHandler CreateSut() => new(_repository, _timeProvider);

    private SdkKey Seed()
    {
        var key = SdkKey.Issue("CI", SdkKeyKind.Secret, EnvironmentKey.Development, Admin, Now).Value.Key;
        _repository.Seed(key);

        return key;
    }

    [Fact]
    public async Task HandleAsync_ShouldRevokeTheKey()
    {
        var key = Seed();

        var result = await CreateSut().HandleAsync(key.Id, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(key.IsActive);
        Assert.Equal(Now, key.RevokedAt.Reduce(default(DateTimeOffset)));
        Assert.Equal(1, _repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WithAnUnknownKey_ShouldReportNotFound()
    {
        var result = await CreateSut().HandleAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SdkKeyErrors.NotFound(Guid.Empty).Code, result.Error.Code);
        Assert.Equal(0, _repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_ForAnAlreadyRevokedKey_ShouldConflictWithoutSaving()
    {
        var key = Seed();
        await CreateSut().HandleAsync(key.Id, TestContext.Current.CancellationToken);

        var result = await CreateSut().HandleAsync(key.Id, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SdkKeyErrors.AlreadyRevoked.Code, result.Error.Code);
        Assert.Equal(1, _repository.SaveChangesCallCount);
    }

    /// <summary>
    /// Revoking keeps the row. A key that stopped working and a key that never existed are
    /// different situations, and the console has to be able to say which one it is looking at.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ShouldNotDeleteTheKey()
    {
        var key = Seed();

        await CreateSut().HandleAsync(key.Id, TestContext.Current.CancellationToken);

        Assert.Equal(key.Id, Assert.Single(_repository.Committed).Id);
    }
}
