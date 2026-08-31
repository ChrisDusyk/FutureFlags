using FutureFlags.Domain.Environments;
using FutureFlags.Domain.Flags;
using FutureFlags.Domain.Shared;
using FutureFlags.Server.Features.Flags.UpdateFlag;
using FutureFlags.Server.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace FutureFlags.Server.Tests.Features.Flags.UpdateFlag;

public class UpdateFlagHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid CausedBy = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly EnvironmentKey[] Nowhere = [];

    private readonly FakeFeatureFlagRepository _repository = new();
    private readonly FakeTimeProvider _timeProvider = new(Now);

    private UpdateFlagHandler CreateSut() => new(_repository, _timeProvider);

    private static FlagKey Key(string value) => FlagKey.Create(value).Value;

    private FeatureFlag Seed(string name = "New checkout", string? description = "Old description.")
    {
        var flag = FeatureFlag.Create("new-checkout", name, description, Nowhere, Now, CausedBy).Value;
        _repository.Seed(flag);

        return flag;
    }

    [Fact]
    public async Task HandleAsync_WithValidCommand_ShouldUpdateAndPersist()
    {
        var flag = Seed();
        _timeProvider.SetUtcNow(Now.AddHours(1));

        var result = await CreateSut().HandleAsync(
            new UpdateFlagCommand(Key("new-checkout"), "Renamed checkout", "New description.", CausedBy),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("Renamed checkout", result.Value.Name);
        Assert.Equal("New description.", result.Value.Description);
        Assert.Equal(Now.AddHours(1), result.Value.UpdatedAt);
        Assert.Equal("Renamed checkout", flag.Name);
        Assert.Equal(1, _repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_ShouldNeverAcceptAKeyChange()
    {
        // UpdateFlagCommand carries no Key property to send — the route segment is the only
        // source of identity, so this asserts the response's key never moves regardless of edit.
        Seed();

        var result = await CreateSut().HandleAsync(
            new UpdateFlagCommand(Key("new-checkout"), "Renamed checkout", "New description.", CausedBy),
            TestContext.Current.CancellationToken);

        Assert.Equal("new-checkout", result.Value.Key);
    }

    [Fact]
    public async Task HandleAsync_WithAnUnknownKey_ShouldReturnNotFoundAndNotPersist()
    {
        var result = await CreateSut().HandleAsync(
            new UpdateFlagCommand(Key("nothing-here"), "New name", null, CausedBy),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Flag.NotFound", result.Error.Code);
        Assert.Equal(0, _repository.SaveChangesCallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_WithMissingName_ShouldReturnValidationErrorAndNotPersist(string? name)
    {
        Seed();

        var result = await CreateSut().HandleAsync(
            new UpdateFlagCommand(Key("new-checkout"), name, "New description.", CausedBy),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Flag.Name.Required", result.Error.Code);
        Assert.Equal(0, _repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WhenNameAndDescriptionAreUnchanged_ShouldStillSaveButNotMoveTheTimestamp()
    {
        var flag = Seed("New checkout", "Same description.");
        _timeProvider.SetUtcNow(Now.AddHours(1));

        var result = await CreateSut().HandleAsync(
            new UpdateFlagCommand(Key("new-checkout"), "New checkout", "Same description.", CausedBy),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, result.Value.UpdatedAt);
        Assert.Equal(Now, flag.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_ShouldNotTouchStates()
    {
        Seed();

        var result = await CreateSut().HandleAsync(
            new UpdateFlagCommand(Key("new-checkout"), "Renamed checkout", "New description.", CausedBy),
            TestContext.Current.CancellationToken);

        Assert.All(result.Value.States, state => Assert.False(state.IsEnabled));
    }

    [Fact]
    public async Task HandleAsync_WhenSaveFails_ShouldReturnTheFailure()
    {
        Seed();
        _repository.FailNextSaveWith(Error.Failure("Store.Unavailable", "The store did not accept the write."));

        var result = await CreateSut().HandleAsync(
            new UpdateFlagCommand(Key("new-checkout"), "Renamed checkout", "New description.", CausedBy),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("Store.Unavailable", result.Error.Code);
    }
}
