using FeatureFlags.Domain.Environments;
using FeatureFlags.Domain.Flags;
using FeatureFlags.Domain.Segments;
using FeatureFlags.Evaluation;
using FeatureFlags.Server.Evaluation;
using FeatureFlags.Server.Features.Evaluation.EvaluateForContext;
using FeatureFlags.Server.Tests.Fakes;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace FeatureFlags.Server.Tests.Features.Evaluation.EvaluateForContext;

/// <summary>
/// The browser's half of the split: the context comes in, booleans go out, and no segment
/// definition leaves the server.
/// </summary>
public class EvaluateForContextHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeFlagViewRepository _flags = new();
    private readonly FakeSegmentViewRepository _segments = new();

    private EvaluateForContextHandler CreateSut() => new(new RulesetProvider(
        _flags,
        _segments,
        new ServiceCollection().AddHybridCache().Services
            .BuildServiceProvider()
            .GetRequiredService<HybridCache>()));

    private FlagKey SeedFlag(string key, bool enabled = true)
    {
        var flagKey = FlagKey.Create(key).Value;

        _flags.Seed(new FlagView(
            Guid.CreateVersion7(), flagKey, key, string.Empty, Now, Now,
            [.. EnvironmentKey.All.Select(environment => new FlagStateView(environment, enabled, [], Now))]));

        return flagKey;
    }

    private SegmentKey SeedSegment(string key, SegmentDefinition definition)
    {
        var segmentKey = SegmentKey.Create(key).Value;

        _segments.Seed(new SegmentView(
            Guid.CreateVersion7(), segmentKey, key, string.Empty, definition, Now, Now));

        return segmentKey;
    }

    private async Task<EvaluateForContextResponse> EvaluateAsync(FlagContext context) =>
        (await CreateSut().HandleAsync(
            new EvaluateForContextQuery(EnvironmentKey.Production, context),
            TestContext.Current.CancellationToken)).Value;

    private static FlagContext Context(string? key, params (string Name, AttributeValue Value)[] attributes) =>
        new(key, attributes.ToDictionary(pair => pair.Name, pair => pair.Value));

    [Fact]
    public async Task HandleAsync_ForAnUntargetedFlag_ShouldAnswerTheSameForEverybody()
    {
        SeedFlag("dark-mode");

        Assert.True((await EvaluateAsync(FlagContext.Empty)).Flags["dark-mode"]);
        Assert.True((await EvaluateAsync(Context("user-17"))).Flags["dark-mode"]);
    }

    [Fact]
    public async Task HandleAsync_ForATargetedFlag_ShouldAnswerPerContext()
    {
        var flag = SeedFlag("new-checkout");
        var segment = SeedSegment("pro-users", SegmentDefinition.Create(
            [], [], [SegmentCondition.Create("plan", "equals", [AttributeValue.OfText("pro")]).Value]).Value);
        _flags.SetTargeting(flag, EnvironmentKey.Production, [segment], Now);

        var matching = await EvaluateAsync(Context("user-17", ("plan", AttributeValue.OfText("pro"))));
        var notMatching = await EvaluateAsync(Context("user-99", ("plan", AttributeValue.OfText("free"))));

        Assert.True(matching.Flags["new-checkout"]);
        Assert.False(notMatching.Flags["new-checkout"]);
    }

    [Fact]
    public async Task HandleAsync_ForATargetedFlag_WithNoContextAtAll_ShouldAnswerFalse()
    {
        var flag = SeedFlag("new-checkout");
        var segment = SeedSegment("pro-users", SegmentDefinition.Create(
            [], [], [SegmentCondition.Create("plan", "equals", [AttributeValue.OfText("pro")]).Value]).Value);
        _flags.SetTargeting(flag, EnvironmentKey.Production, [segment], Now);

        Assert.False((await EvaluateAsync(FlagContext.Empty)).Flags["new-checkout"]);
    }

    [Fact]
    public async Task HandleAsync_ForAFlagThatIsOff_ShouldAnswerFalseHoweverWellTheContextMatches()
    {
        var flag = SeedFlag("new-checkout", enabled: false);
        var segment = SeedSegment("everyone", SegmentDefinition.Create(["user-17"], [], []).Value);
        _flags.SetTargeting(flag, EnvironmentKey.Production, [segment], Now);

        Assert.False((await EvaluateAsync(Context("user-17"))).Flags["new-checkout"]);
    }

    [Fact]
    public async Task HandleAsync_ShouldMatchOnAnIncludedKey()
    {
        var flag = SeedFlag("new-checkout");
        var segment = SeedSegment("debugging", SegmentDefinition.Create(["user-17"], [], []).Value);
        _flags.SetTargeting(flag, EnvironmentKey.Production, [segment], Now);

        Assert.True((await EvaluateAsync(Context("user-17"))).Flags["new-checkout"]);
        Assert.False((await EvaluateAsync(Context("user-99"))).Flags["new-checkout"]);
    }

    [Fact]
    public async Task HandleAsync_ShouldAnswerForEveryFlagAndNotOnlyTheTargetedOnes()
    {
        SeedFlag("dark-mode");
        var flag = SeedFlag("new-checkout");
        var segment = SeedSegment("nobody", SegmentDefinition.Empty);
        _flags.SetTargeting(flag, EnvironmentKey.Production, [segment], Now);

        var evaluated = await EvaluateAsync(Context("user-17"));

        // A flag missing from the map and a flag that is off are different answers to a client
        // holding a default.
        Assert.Equal(2, evaluated.Flags.Count);
        Assert.True(evaluated.Flags["dark-mode"]);
        Assert.False(evaluated.Flags["new-checkout"]);
    }

    [Fact]
    public async Task HandleAsync_ShouldReportTheRulesetVersionItAnsweredFrom()
    {
        SeedFlag("dark-mode");

        var first = await EvaluateAsync(Context("user-17"));
        var second = await EvaluateAsync(Context("user-99"));

        // The same ruleset answered both, so a client can cache by (version, context) and know when
        // its cached answers are worth keeping. It is in the body rather than an ETag because a
        // conditional POST would have to answer 412 rather than 304 to stay within RFC 9110.
        Assert.Equal(first.RulesetVersion, second.RulesetVersion);
        Assert.StartsWith("\"", first.RulesetVersion);
    }

    [Fact]
    public async Task HandleAsync_ShouldReportTheEnvironmentTheKeyIsScopedTo()
    {
        SeedFlag("dark-mode");

        Assert.Equal("prod", (await EvaluateAsync(FlagContext.Empty)).Environment);
    }

    [Fact]
    public async Task HandleAsync_WithATargetedSegmentThatNoLongerExists_ShouldAnswerFalseRatherThanThrow()
    {
        // A segment can be retired between the write that targeted it and this read. Every engine
        // treats a key it cannot resolve as a non-match, which is the safe direction.
        var flag = SeedFlag("new-checkout");
        _flags.SetTargeting(flag, EnvironmentKey.Production, [SegmentKey.Create("deleted-yesterday").Value], Now);

        Assert.False((await EvaluateAsync(Context("user-17"))).Flags["new-checkout"]);
    }
}
