using FeatureFlags.Domain.Environments;
using FeatureFlags.Domain.Flags;
using FeatureFlags.Domain.Shared;
using FeatureFlags.Server.Features.Flags.GetFlag;
using FeatureFlags.Server.Tests.Fakes;

namespace FeatureFlags.Server.Tests.Features.Flags.GetFlag;

public class GetFlagHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeFlagViewRepository _repository = new();

    private GetFlagHandler CreateSut() => new(_repository);

    private FlagView Seed(string key, string name, string? description, params EnvironmentKey[] enabledIn)
    {
        var flagKey = FlagKey.Create(key).Value;
        var enabled = enabledIn.ToHashSet();

        var view = new FlagView(
            Guid.CreateVersion7(),
            flagKey,
            name,
            description ?? string.Empty,
            Now,
            Now,
            [.. EnvironmentKey.All.Select(environment => new FlagStateView(environment, enabled.Contains(environment), [], Now))]);

        _repository.Seed(view);
        return view;
    }

    [Fact]
    public async Task HandleAsync_WithAnExistingFlag_ShouldReturnItsFullDetails()
    {
        var flag = Seed("new-checkout", "New checkout", "Notes.", EnvironmentKey.Development);

        var result = await CreateSut().HandleAsync(
            new GetFlagQuery(flag.Key),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);

        var response = result.Value;
        Assert.Equal(flag.Id, response.Id);
        Assert.Equal("new-checkout", response.Key);
        Assert.Equal("New checkout", response.Name);
        Assert.Equal("Notes.", response.Description);
        Assert.Equal(Now, response.CreatedAt);
        Assert.Equal(Now, response.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnAStateForEveryEnvironment()
    {
        var flag = Seed("new-checkout", "New checkout", null, EnvironmentKey.Development);

        var result = await CreateSut().HandleAsync(
            new GetFlagQuery(flag.Key),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            EnvironmentKey.All.Select(environment => environment.Value),
            result.Value.States.Select(state => state.Environment));
        Assert.True(result.Value.States.Single(state => state.Environment == "dev").IsEnabled);
        Assert.False(result.Value.States.Single(state => state.Environment == "prod").IsEnabled);
    }

    [Fact]
    public async Task HandleAsync_WithAnUnknownKey_ShouldReturnNotFound()
    {
        var result = await CreateSut().HandleAsync(
            new GetFlagQuery(FlagKey.Create("nothing-here").Value),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Flag.NotFound", result.Error.Code);
    }
}
