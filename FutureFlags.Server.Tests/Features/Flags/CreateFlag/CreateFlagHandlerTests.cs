using FutureFlags.Domain.Environments;
using FutureFlags.Domain.Flags;
using FutureFlags.Domain.Shared;
using FutureFlags.Server.Features.Flags.CreateFlag;
using FutureFlags.Server.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace FutureFlags.Server.Tests.Features.Flags.CreateFlag;

public class CreateFlagHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid CausedBy = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly EnvironmentKey[] Nowhere = [];

    private readonly FakeFeatureFlagRepository _repository = new();
    private readonly FakeTimeProvider _timeProvider = new(Now);

    private CreateFlagHandler CreateSut() => new(_repository, _timeProvider);

    [Fact]
    public async Task HandleAsync_WithValidCommand_ShouldPersistFlagAndReturnResponse()
    {
        var command = new CreateFlagCommand("new-checkout", "New checkout", "Rewritten checkout.", [EnvironmentKey.Development], CausedBy);

        var result = await CreateSut().HandleAsync(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);

        var response = result.Value;
        Assert.Equal("new-checkout", response.Key);
        Assert.Equal("New checkout", response.Name);
        Assert.Equal("Rewritten checkout.", response.Description);
        Assert.Equal(Now, response.CreatedAt);
        Assert.Equal(Now, response.UpdatedAt);
        Assert.NotEqual(Guid.Empty, response.Id);

        var persisted = Assert.Single(_repository.Committed);
        Assert.Equal(response.Id, persisted.Id);
        Assert.Equal(1, _repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_ShouldStampTimestampsFromTimeProvider()
    {
        _timeProvider.SetUtcNow(Now.AddDays(3));
        var command = new CreateFlagCommand("new-checkout", "New checkout", null, Nowhere, CausedBy);

        var result = await CreateSut().HandleAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(Now.AddDays(3), result.Value.CreatedAt);
    }

    [Fact]
    public async Task HandleAsync_ShouldNormalizeKey()
    {
        var command = new CreateFlagCommand("  NEW-Checkout  ", "New checkout", null, Nowhere, CausedBy);

        var result = await CreateSut().HandleAsync(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-checkout", result.Value.Key);
    }

    [Fact]
    public async Task HandleAsync_WhenKeyAlreadyExists_ShouldReturnConflictAndNotPersist()
    {
        _repository.Seed(FeatureFlag.Create("new-checkout", "Existing", null, Nowhere, Now, CausedBy).Value);
        var command = new CreateFlagCommand("new-checkout", "New checkout", null, Nowhere, CausedBy);

        var result = await CreateSut().HandleAsync(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Flag.DuplicateKey", result.Error.Code);
        Assert.Single(_repository.Committed);
        Assert.Equal(0, _repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WhenKeyDiffersOnlyByCasing_ShouldReturnConflict()
    {
        _repository.Seed(FeatureFlag.Create("new-checkout", "Existing", null, Nowhere, Now, CausedBy).Value);
        var command = new CreateFlagCommand("NEW-CHECKOUT", "New checkout", null, Nowhere, CausedBy);

        var result = await CreateSut().HandleAsync(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Theory]
    [InlineData(null, "Name", "Flag.Key.Required")]
    [InlineData("not a key", "Name", "Flag.Key.InvalidFormat")]
    [InlineData("new-checkout", null, "Flag.Name.Required")]
    [InlineData("new-checkout", "  ", "Flag.Name.Required")]
    public async Task HandleAsync_WithInvalidCommand_ShouldReturnValidationErrorAndNotPersist(
        string? key,
        string? name,
        string expectedCode)
    {
        var command = new CreateFlagCommand(key, name, null, Nowhere, CausedBy);

        var result = await CreateSut().HandleAsync(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal(expectedCode, result.Error.Code);
        Assert.Empty(_repository.Committed);
        Assert.Equal(0, _repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WhenSaveHitsTheUniqueIndex_ShouldReturnConflictNotThrow()
    {
        // The key was free at the check but taken by the time the insert ran.
        var key = FlagKey.Create("new-checkout").Value;
        _repository.FailNextSaveWith(FlagErrors.DuplicateKey(key));
        var command = new CreateFlagCommand("new-checkout", "New checkout", null, Nowhere, CausedBy);

        var result = await CreateSut().HandleAsync(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Flag.DuplicateKey", result.Error.Code);
        Assert.Empty(_repository.Committed);
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnAStateForEveryEnvironment()
    {
        var command = new CreateFlagCommand("new-checkout", "New checkout", null, [EnvironmentKey.Development], CausedBy);

        var result = await CreateSut().HandleAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(
            EnvironmentKey.All.Select(environment => environment.Value),
            result.Value.States.Select(state => state.Environment));
    }

    [Fact]
    public async Task HandleAsync_ShouldEnableOnlyTheEnvironmentsAskedFor()
    {
        var command = new CreateFlagCommand("new-checkout", "New checkout", null, [EnvironmentKey.Development], CausedBy);

        var result = await CreateSut().HandleAsync(command, TestContext.Current.CancellationToken);

        var persisted = Assert.Single(_repository.Committed);
        Assert.True(persisted.IsEnabledIn(EnvironmentKey.Development));
        Assert.False(persisted.IsEnabledIn(EnvironmentKey.Staging));
        Assert.False(persisted.IsEnabledIn(EnvironmentKey.Production));
    }

    [Fact]
    public async Task HandleAsync_WithNoEnvironments_ShouldPersistTheFlagOffEverywhere()
    {
        var command = new CreateFlagCommand("new-checkout", "New checkout", null, Nowhere, CausedBy);

        var result = await CreateSut().HandleAsync(command, TestContext.Current.CancellationToken);

        Assert.All(result.Value.States, state => Assert.False(state.IsEnabled));
        Assert.All(Assert.Single(_repository.Committed).States, state => Assert.False(state.IsEnabled));
    }
}
