using FeatureFlags.Domain.Environments;
using FeatureFlags.Domain.Flags;
using FeatureFlags.Domain.Segments;
using FeatureFlags.Server.Evaluation;
using FeatureFlags.Server.Features.Flags.EvaluateFlags;
using FeatureFlags.Server.Tests.Fakes;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace FeatureFlags.Server.Tests.Features.Flags.EvaluateFlags;

public class EvaluateFlagsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeFlagViewRepository _flags = new();
    private readonly FakeSegmentViewRepository _segments = new();

    /// <summary>
    /// A fresh cache per handler, so a test that wants to see a change reaches for a new one rather
    /// than waiting out an expiry. With no <c>IDistributedCache</c> registered, HybridCache runs on
    /// its in-process tier alone — which is the same code path the second tier sits behind.
    /// </summary>
    private EvaluateFlagsHandler CreateSut() => new(new RulesetProvider(
        _flags,
        _segments,
        new ServiceCollection().AddHybridCache().Services
            .BuildServiceProvider()
            .GetRequiredService<HybridCache>()));

    private FlagView Seed(string key, params EnvironmentKey[] enabledIn)
    {
        var flagKey = FlagKey.Create(key).Value;
        var enabled = enabledIn.ToHashSet();

        var view = new FlagView(
            Guid.CreateVersion7(),
            flagKey,
            key,
            string.Empty,
            Now,
            Now,
            [.. EnvironmentKey.All.Select(environment => new FlagStateView(environment, enabled.Contains(environment), [], Now))]);

        _flags.Seed(view);
        return view;
    }

    private async Task<EvaluatedFlags> EvaluateAsync(EnvironmentKey environment) =>
        (await CreateSut().HandleAsync(
            new EvaluateFlagsQuery(environment),
            TestContext.Current.CancellationToken)).Value;

    [Fact]
    public async Task HandleAsync_WithNoFlags_ShouldAnswerWithAnEmptyMap()
    {
        var evaluated = await EvaluateAsync(EnvironmentKey.Development);

        Assert.Equal("dev", evaluated.Response.Environment);
        Assert.Empty(evaluated.Response.Flags);
    }

    [Fact]
    public async Task HandleAsync_ShouldAnswerForTheEnvironmentAsked()
    {
        Seed("new-checkout", EnvironmentKey.Development);
        Seed("dark-mode", EnvironmentKey.Production);

        var development = await EvaluateAsync(EnvironmentKey.Development);
        var production = await EvaluateAsync(EnvironmentKey.Production);

        Assert.True(development.Response.Flags["new-checkout"]);
        Assert.False(development.Response.Flags["dark-mode"]);

        Assert.False(production.Response.Flags["new-checkout"]);
        Assert.True(production.Response.Flags["dark-mode"]);
    }

    [Fact]
    public async Task HandleAsync_ShouldIncludeEveryFlagRegardlessOfState()
    {
        Seed("on", EnvironmentKey.Development);
        Seed("off");

        var evaluated = await EvaluateAsync(EnvironmentKey.Development);

        // A flag missing from the map and a flag that is off are different answers to a client
        // holding a default, so an off flag has to be present and false rather than absent.
        Assert.Equal(2, evaluated.Response.Flags.Count);
        Assert.False(evaluated.Response.Flags["off"]);
    }

    [Fact]
    public async Task HandleAsync_ShouldQuoteTheETag()
    {
        Seed("new-checkout", EnvironmentKey.Development);

        var evaluated = await EvaluateAsync(EnvironmentKey.Development);

        // A bare tag is silently ignored by anything that parses the header properly.
        Assert.StartsWith("\"", evaluated.ETag);
        Assert.EndsWith("\"", evaluated.ETag);
    }

    [Fact]
    public async Task HandleAsync_WithUnchangedFlags_ShouldReturnTheSameETag()
    {
        Seed("new-checkout", EnvironmentKey.Development);

        var first = await EvaluateAsync(EnvironmentKey.Development);
        var second = await EvaluateAsync(EnvironmentKey.Development);

        Assert.Equal(first.ETag, second.ETag);
    }

    [Fact]
    public async Task HandleAsync_AfterAFlagIsToggled_ShouldReturnADifferentETag()
    {
        var flag = Seed("new-checkout", EnvironmentKey.Development);

        var before = await EvaluateAsync(EnvironmentKey.Development);

        _flags.SetEnabled(flag.Key, EnvironmentKey.Development, false, Now.AddHours(1));

        var after = await EvaluateAsync(EnvironmentKey.Development);

        Assert.NotEqual(before.ETag, after.ETag);
        Assert.False(after.Response.Flags["new-checkout"]);
    }

    [Fact]
    public async Task HandleAsync_AfterAFlagIsAdded_ShouldReturnADifferentETag()
    {
        Seed("new-checkout", EnvironmentKey.Development);

        var before = await EvaluateAsync(EnvironmentKey.Development);

        Seed("dark-mode", EnvironmentKey.Development);

        Assert.NotEqual(before.ETag, (await EvaluateAsync(EnvironmentKey.Development)).ETag);
    }

    /// <summary>
    /// Two environments that happen to agree on every flag are still two different answers, and a
    /// client caching by ETag alone would otherwise be told nothing had changed when it switched.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ForDifferentEnvironments_ShouldNotShareAnETag()
    {
        Seed("new-checkout");

        var development = await EvaluateAsync(EnvironmentKey.Development);
        var production = await EvaluateAsync(EnvironmentKey.Production);

        Assert.Equal(development.Response.Flags, production.Response.Flags);
        Assert.NotEqual(development.ETag, production.ETag);
    }

    [Fact]
    public async Task HandleAsync_ForATargetedFlag_ShouldAnswerFalse()
    {
        // The behaviour change this route carries. It answers for nobody in particular, and a flag
        // narrowed to a segment is not on for nobody in particular — so every SDK still reading
        // here sees a newly targeted flag go dark on its next poll. That is the safe direction:
        // the alternative hands the feature to exactly the people it was narrowed away from.
        var flag = Seed("new-checkout", EnvironmentKey.Development);
        _segments.Seed(SegmentKey.Create("beta-testers").Value);
        _flags.SetTargeting(flag.Key, EnvironmentKey.Development, [SegmentKey.Create("beta-testers").Value], Now);

        var evaluated = await EvaluateAsync(EnvironmentKey.Development);

        Assert.False(evaluated.Response.Flags["new-checkout"]);
    }

    [Fact]
    public async Task HandleAsync_WhenOnlyTheTargetingChanges_ShouldStillChangeTheETag()
    {
        // Both answers are the same map — the flag reads false before targeting exists and false
        // after, for different reasons. A tag over the booleans alone would tell a client nothing
        // had changed, and the client that later asks with a context would get a stale answer.
        var flag = Seed("new-checkout");
        _segments.Seed(SegmentKey.Create("beta-testers").Value);

        var before = await EvaluateAsync(EnvironmentKey.Development);

        _flags.SetTargeting(flag.Key, EnvironmentKey.Development, [SegmentKey.Create("beta-testers").Value], Now.AddHours(1));

        var after = await EvaluateAsync(EnvironmentKey.Development);

        Assert.Equal(before.Response.Flags, after.Response.Flags);
        Assert.NotEqual(before.ETag, after.ETag);
    }

    [Fact]
    public async Task HandleAsync_ShouldServeARepeatedCallFromTheCache()
    {
        var flag = Seed("new-checkout", EnvironmentKey.Development);

        var handler = CreateSut();
        var query = new EvaluateFlagsQuery(EnvironmentKey.Development);

        var first = await handler.HandleAsync(query, TestContext.Current.CancellationToken);

        // Changing the flags behind the handler's back: the cached answer is the one that proves
        // the second call did not reach the repository.
        _flags.SetEnabled(flag.Key, EnvironmentKey.Development, false, Now.AddHours(1));

        var second = await handler.HandleAsync(query, TestContext.Current.CancellationToken);

        Assert.Equal(first.Value.ETag, second.Value.ETag);
        Assert.True(second.Value.Response.Flags["new-checkout"]);
    }
}
