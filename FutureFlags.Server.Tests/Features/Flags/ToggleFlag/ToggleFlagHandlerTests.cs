using FutureFlags.Domain.Environments;
using FutureFlags.Domain.Flags;
using FutureFlags.Domain.Shared;
using FutureFlags.Server.Features.Flags.ToggleFlag;
using FutureFlags.Server.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace FutureFlags.Server.Tests.Features.Flags.ToggleFlag;

public class ToggleFlagHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid CausedBy = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly EnvironmentKey[] Nowhere = [];

    private readonly FakeFeatureFlagRepository _repository = new();
    private readonly FakeTimeProvider _timeProvider = new(Now);

    private ToggleFlagHandler CreateSut() => new(_repository, _timeProvider);

    private static FlagKey Key(string value) => FlagKey.Create(value).Value;

    private FeatureFlag Seed(params EnvironmentKey[] enabledIn)
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", null, enabledIn, Now, CausedBy).Value;
        _repository.Seed(flag);

        return flag;
    }

    [Fact]
    public async Task HandleAsync_TurningOn_ShouldEnableAndPersist()
    {
        var flag = Seed(Nowhere);
        _timeProvider.SetUtcNow(Now.AddHours(1));

        var result = await CreateSut().HandleAsync(
            new ToggleFlagCommand(Key("new-checkout"), EnvironmentKey.Production, IsEnabled: true, CausedBy),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsEnabled);
        Assert.Equal("prod", result.Value.Environment);
        Assert.Equal(Now.AddHours(1), result.Value.UpdatedAt);
        Assert.True(flag.IsEnabledIn(EnvironmentKey.Production));
        Assert.Equal(1, _repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_TurningOff_ShouldDisableAndPersist()
    {
        var flag = Seed(EnvironmentKey.Development);

        var result = await CreateSut().HandleAsync(
            new ToggleFlagCommand(Key("new-checkout"), EnvironmentKey.Development, IsEnabled: false, CausedBy),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsEnabled);
        Assert.False(flag.IsEnabledIn(EnvironmentKey.Development));
    }

    [Fact]
    public async Task HandleAsync_ShouldMoveOnlyTheNamedEnvironment()
    {
        var flag = Seed(Nowhere);

        await CreateSut().HandleAsync(
            new ToggleFlagCommand(Key("new-checkout"), EnvironmentKey.Production, IsEnabled: true, CausedBy),
            TestContext.Current.CancellationToken);

        Assert.False(flag.IsEnabledIn(EnvironmentKey.Development));
        Assert.False(flag.IsEnabledIn(EnvironmentKey.Staging));
    }

    [Fact]
    public async Task HandleAsync_SentTwice_ShouldSettleOnTheSameState()
    {
        // The point of PUT-ing the state rather than POST-ing a flip: a retried request after a
        // dropped response must not turn the flag back off.
        var flag = Seed(Nowhere);
        var command = new ToggleFlagCommand(Key("new-checkout"), EnvironmentKey.Production, IsEnabled: true, CausedBy);

        await CreateSut().HandleAsync(command, TestContext.Current.CancellationToken);
        var second = await CreateSut().HandleAsync(command, TestContext.Current.CancellationToken);

        Assert.True(second.IsSuccess);
        Assert.True(second.Value.IsEnabled);
        Assert.True(flag.IsEnabledIn(EnvironmentKey.Production));
    }

    [Fact]
    public async Task HandleAsync_WhenAlreadyInThatState_ShouldNotMoveTheTimestamp()
    {
        Seed(EnvironmentKey.Development);
        _timeProvider.SetUtcNow(Now.AddHours(1));

        var result = await CreateSut().HandleAsync(
            new ToggleFlagCommand(Key("new-checkout"), EnvironmentKey.Development, IsEnabled: true, CausedBy),
            TestContext.Current.CancellationToken);

        Assert.Equal(Now, result.Value.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_WithAnUnknownKey_ShouldReturnNotFoundAndNotPersist()
    {
        var result = await CreateSut().HandleAsync(
            new ToggleFlagCommand(Key("nothing-here"), EnvironmentKey.Development, IsEnabled: true, CausedBy),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Flag.NotFound", result.Error.Code);
        Assert.Equal(0, _repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WhenSaveFails_ShouldReturnTheFailure()
    {
        Seed(Nowhere);
        _repository.FailNextSaveWith(Error.Failure("Store.Unavailable", "The store did not accept the write."));

        var result = await CreateSut().HandleAsync(
            new ToggleFlagCommand(Key("new-checkout"), EnvironmentKey.Production, IsEnabled: true, CausedBy),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("Store.Unavailable", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenSaveReportsAConcurrencyConflict_ShouldReturnConflict()
    {
        // Two request-scoped repositories reading the same flag's event stream at the same version
        // is a real race under event sourcing, not a fake-only case — FeatureFlagRepositoryConcurrencyTests
        // proves the store actually produces this; this proves the handler forwards it correctly.
        var key = Key("new-checkout");
        Seed(Nowhere);
        _repository.FailNextSaveWith(FlagErrors.ConcurrencyConflict(key));

        var result = await CreateSut().HandleAsync(
            new ToggleFlagCommand(key, EnvironmentKey.Production, IsEnabled: true, CausedBy),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Flag.ConcurrencyConflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_ShouldNotTouchTheFlagsOwnUpdatedAt()
    {
        var flag = Seed(Nowhere);
        _timeProvider.SetUtcNow(Now.AddDays(2));

        await CreateSut().HandleAsync(
            new ToggleFlagCommand(Key("new-checkout"), EnvironmentKey.Production, IsEnabled: true, CausedBy),
            TestContext.Current.CancellationToken);

        Assert.Equal(Now, flag.UpdatedAt);
    }
}
