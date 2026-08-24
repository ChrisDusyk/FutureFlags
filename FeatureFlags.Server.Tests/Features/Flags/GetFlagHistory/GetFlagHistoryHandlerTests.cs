using FeatureFlags.Domain.Environments;
using FeatureFlags.Domain.Flags;
using FeatureFlags.Domain.Flags.Events;
using FeatureFlags.Domain.Shared;
using FeatureFlags.Domain.Users;
using FeatureFlags.Server.Features.Flags.GetFlagHistory;
using FeatureFlags.Server.Tests.Fakes;

namespace FeatureFlags.Server.Tests.Features.Flags.GetFlagHistory;

public class GetFlagHistoryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Ada = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly FakeFlagViewRepository _viewRepository = new();
    private readonly FakeUserRepository _userRepository = new();

    private GetFlagHistoryHandler CreateSut() => new(_viewRepository, _userRepository);

    private FlagView SeedFlag(string key = "new-checkout")
    {
        var view = new FlagView(
            Guid.CreateVersion7(),
            FlagKey.Create(key).Value,
            "New checkout",
            string.Empty,
            Now,
            Now,
            [.. EnvironmentKey.All.Select(environment => new FlagStateView(environment, false, [], Now))]);

        _viewRepository.Seed(view);
        return view;
    }

    [Fact]
    public async Task HandleAsync_WithAnUnknownKey_ShouldReturnNotFound()
    {
        var result = await CreateSut().HandleAsync(
            new GetFlagHistoryQuery(FlagKey.Create("nothing-here").Value),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Flag.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_WithNoHistory_ShouldReturnAnEmptyList()
    {
        var flag = SeedFlag();

        var result = await CreateSut().HandleAsync(new GetFlagHistoryQuery(flag.Key), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Entries);
    }

    [Fact]
    public async Task HandleAsync_ShouldResolveTheActorsNameFromTheUserMirror()
    {
        var flag = SeedFlag();
        _userRepository.Seed(User.FromPersisted(Ada, "ada@example.com", "Ada Lovelace", UserRole.User, Now, Now));
        _viewRepository.SeedHistory(flag.Id, new FlagCreatedEvent(flag.Id, flag.Key, "New checkout", "", Now, Ada));

        var result = await CreateSut().HandleAsync(new GetFlagHistoryQuery(flag.Key), TestContext.Current.CancellationToken);

        Assert.Equal("Ada Lovelace", Assert.Single(result.Value.Entries).CausedByName);
    }

    [Fact]
    public async Task HandleAsync_WhenTheUserHasNoDisplayName_ShouldFallBackToEmail()
    {
        var flag = SeedFlag();
        _userRepository.Seed(User.FromPersisted(Ada, "ada@example.com", "", UserRole.User, Now, Now));
        _viewRepository.SeedHistory(flag.Id, new FlagCreatedEvent(flag.Id, flag.Key, "New checkout", "", Now, Ada));

        var result = await CreateSut().HandleAsync(new GetFlagHistoryQuery(flag.Key), TestContext.Current.CancellationToken);

        Assert.Equal("ada@example.com", Assert.Single(result.Value.Entries).CausedByName);
    }

    [Fact]
    public async Task HandleAsync_WhenAnEventHasNoCausedBy_ShouldReportNoName()
    {
        // A backfilled, pre-attribution event — the migration's lossy backfill leaves these null.
        var flag = SeedFlag();
        _viewRepository.SeedHistory(flag.Id, new FlagCreatedEvent(flag.Id, flag.Key, "New checkout", "", Now, null));

        var result = await CreateSut().HandleAsync(new GetFlagHistoryQuery(flag.Key), TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(result.Value.Entries).CausedByName);
    }

    [Fact]
    public async Task HandleAsync_WhenTheCausedByUserNoLongerExists_ShouldReportNoName()
    {
        var flag = SeedFlag();
        _viewRepository.SeedHistory(flag.Id, new FlagCreatedEvent(flag.Id, flag.Key, "New checkout", "", Now, Ada));

        var result = await CreateSut().HandleAsync(new GetFlagHistoryQuery(flag.Key), TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(result.Value.Entries).CausedByName);
    }

    [Fact]
    public async Task HandleAsync_ShouldResolveEachDistinctActorOnlyOnce()
    {
        var flag = SeedFlag();
        _userRepository.Seed(User.FromPersisted(Ada, "ada@example.com", "Ada Lovelace", UserRole.User, Now, Now));
        _viewRepository.SeedHistory(
            flag.Id,
            new FlagStateChangedEvent(flag.Id, EnvironmentKey.Staging, true, Now.AddHours(2), Ada),
            new FlagStateChangedEvent(flag.Id, EnvironmentKey.Development, true, Now.AddHours(1), Ada),
            new FlagCreatedEvent(flag.Id, flag.Key, "New checkout", "", Now, Ada));

        var result = await CreateSut().HandleAsync(new GetFlagHistoryQuery(flag.Key), TestContext.Current.CancellationToken);

        Assert.All(result.Value.Entries, entry => Assert.Equal("Ada Lovelace", entry.CausedByName));
    }

    [Fact]
    public async Task HandleAsync_ShouldMapAFlagCreatedEventWithItsNameAndDescription()
    {
        var flag = SeedFlag();
        _viewRepository.SeedHistory(flag.Id, new FlagCreatedEvent(flag.Id, flag.Key, "New checkout", "Notes.", Now, null));

        var result = await CreateSut().HandleAsync(new GetFlagHistoryQuery(flag.Key), TestContext.Current.CancellationToken);

        var entry = Assert.Single(result.Value.Entries);
        Assert.Equal("FlagCreated", entry.EventType);
        Assert.Equal(Now, entry.OccurredAt);
        Assert.Equal("New checkout", entry.Name);
        Assert.Equal("Notes.", entry.Description);
        Assert.Null(entry.Environment);
        Assert.Null(entry.IsEnabled);
    }

    [Fact]
    public async Task HandleAsync_ShouldMapAFlagDetailsChangedEventWithItsNameAndDescription()
    {
        var flag = SeedFlag();
        _viewRepository.SeedHistory(flag.Id, new FlagDetailsChangedEvent(flag.Id, "Renamed", "New notes.", Now, null));

        var result = await CreateSut().HandleAsync(new GetFlagHistoryQuery(flag.Key), TestContext.Current.CancellationToken);

        var entry = Assert.Single(result.Value.Entries);
        Assert.Equal("FlagDetailsChanged", entry.EventType);
        Assert.Equal("Renamed", entry.Name);
        Assert.Equal("New notes.", entry.Description);
        Assert.Null(entry.Environment);
        Assert.Null(entry.IsEnabled);
    }

    [Fact]
    public async Task HandleAsync_ShouldMapAFlagStateChangedEventWithItsEnvironmentAndState()
    {
        var flag = SeedFlag();
        _viewRepository.SeedHistory(flag.Id, new FlagStateChangedEvent(flag.Id, EnvironmentKey.Production, true, Now, null));

        var result = await CreateSut().HandleAsync(new GetFlagHistoryQuery(flag.Key), TestContext.Current.CancellationToken);

        var entry = Assert.Single(result.Value.Entries);
        Assert.Equal("FlagStateChanged", entry.EventType);
        Assert.Equal("prod", entry.Environment);
        Assert.True(entry.IsEnabled);
        Assert.Null(entry.Name);
        Assert.Null(entry.Description);
    }

    [Fact]
    public async Task HandleAsync_ShouldPreserveTheRepositorysOrdering()
    {
        // The fake, like the real repository, hands back events in whatever order it was seeded
        // with — the handler must not reorder them, since "newest first" is the repository's job.
        var flag = SeedFlag();
        _viewRepository.SeedHistory(
            flag.Id,
            new FlagStateChangedEvent(flag.Id, EnvironmentKey.Production, true, Now.AddHours(2), null),
            new FlagStateChangedEvent(flag.Id, EnvironmentKey.Staging, true, Now.AddHours(1), null),
            new FlagCreatedEvent(flag.Id, flag.Key, "New checkout", "", Now, null));

        var result = await CreateSut().HandleAsync(new GetFlagHistoryQuery(flag.Key), TestContext.Current.CancellationToken);

        Assert.Equal(
            ["FlagStateChanged", "FlagStateChanged", "FlagCreated"],
            result.Value.Entries.Select(entry => entry.EventType));
    }
}
