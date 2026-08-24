using FeatureFlags.Domain.Environments;
using FeatureFlags.Domain.Flags;
using FeatureFlags.Server.Features.Flags.ListFlags;
using FeatureFlags.Server.Tests.Fakes;

namespace FeatureFlags.Server.Tests.Features.Flags.ListFlags;

public class ListFlagsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    private static readonly EnvironmentKey[] Nowhere = [];

    private readonly FakeFlagViewRepository _repository = new();

    private ListFlagsHandler CreateSut() => new(_repository);

    private FlagView Seed(string key, params EnvironmentKey[] enabledIn) => Seed(key, key, null, enabledIn);

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
    public async Task HandleAsync_WithNoFlags_ShouldReturnAnEmptyList()
    {
        // No flags is the ordinary first run, not a failure the screen has to apologize for.
        var result = await CreateSut().HandleAsync(
            new ListFlagsQuery(EnvironmentKey.Development),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Flags);
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnEveryFlag()
    {
        Seed("alpha");
        Seed("beta");

        var result = await CreateSut().HandleAsync(
            new ListFlagsQuery(EnvironmentKey.Development),
            TestContext.Current.CancellationToken);

        Assert.Equal(["alpha", "beta"], result.Value.Flags.Select(flag => flag.Key));
    }

    [Fact]
    public async Task HandleAsync_ShouldAnswerIsEnabledForTheRequestedEnvironment()
    {
        Seed("new-checkout", EnvironmentKey.Development);

        var inDevelopment = await CreateSut().HandleAsync(
            new ListFlagsQuery(EnvironmentKey.Development),
            TestContext.Current.CancellationToken);

        var inProduction = await CreateSut().HandleAsync(
            new ListFlagsQuery(EnvironmentKey.Production),
            TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(inDevelopment.Value.Flags).IsEnabled);
        Assert.False(Assert.Single(inProduction.Value.Flags).IsEnabled);
    }

    [Fact]
    public async Task HandleAsync_ShouldListAFlagInEveryEnvironmentEvenWhereItIsOff()
    {
        // A flag exists everywhere the moment it is created. Hiding it where it happens to be off
        // would make production look like the flag was never made.
        Seed("new-checkout", EnvironmentKey.Development);

        var result = await CreateSut().HandleAsync(
            new ListFlagsQuery(EnvironmentKey.Production),
            TestContext.Current.CancellationToken);

        Assert.Equal("new-checkout", Assert.Single(result.Value.Flags).Key);
    }

    [Fact]
    public async Task HandleAsync_ShouldReportTheStateTimestampNotTheFlagsOwn()
    {
        var flag = Seed("new-checkout", Nowhere);
        var later = Now.AddHours(5);
        _repository.SetEnabled(flag.Key, EnvironmentKey.Production, isEnabled: true, later);

        var inProduction = await CreateSut().HandleAsync(
            new ListFlagsQuery(EnvironmentKey.Production),
            TestContext.Current.CancellationToken);

        var inDevelopment = await CreateSut().HandleAsync(
            new ListFlagsQuery(EnvironmentKey.Development),
            TestContext.Current.CancellationToken);

        Assert.Equal(later, Assert.Single(inProduction.Value.Flags).UpdatedAt);
        Assert.Equal(Now, Assert.Single(inDevelopment.Value.Flags).UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_ShouldEchoTheEnvironmentItAnsweredFor()
    {
        var result = await CreateSut().HandleAsync(
            new ListFlagsQuery(EnvironmentKey.Staging),
            TestContext.Current.CancellationToken);

        Assert.Equal("stg", result.Value.Environment);
    }

    [Fact]
    public async Task HandleAsync_ShouldCarryNameAndDescriptionUnchangedAcrossEnvironments()
    {
        var flag = Seed("new-checkout", "New checkout", "Notes.", EnvironmentKey.Development);

        var inProduction = await CreateSut().HandleAsync(
            new ListFlagsQuery(EnvironmentKey.Production),
            TestContext.Current.CancellationToken);

        var summary = Assert.Single(inProduction.Value.Flags);
        Assert.Equal("New checkout", summary.Name);
        Assert.Equal("Notes.", summary.Description);
        Assert.Equal(flag.Id, summary.Id);
    }
}
