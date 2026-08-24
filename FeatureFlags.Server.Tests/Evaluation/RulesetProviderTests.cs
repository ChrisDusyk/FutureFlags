using System.Text.Json;
using FeatureFlags.Domain.Environments;
using FeatureFlags.Domain.Flags;
using FeatureFlags.Domain.Segments;
using FeatureFlags.Evaluation;
using FeatureFlags.Server.Evaluation;
using FeatureFlags.Server.Tests.Fakes;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace FeatureFlags.Server.Tests.Evaluation;

public class RulesetProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeFlagViewRepository _flags = new();
    private readonly FakeSegmentViewRepository _segments = new();

    private RulesetProvider CreateSut() => new(
        _flags,
        _segments,
        new ServiceCollection().AddHybridCache().Services
            .BuildServiceProvider()
            .GetRequiredService<HybridCache>());

    private FlagKey SeedFlag(string key, params EnvironmentKey[] enabledIn)
    {
        var flagKey = FlagKey.Create(key).Value;
        var enabled = enabledIn.ToHashSet();

        _flags.Seed(new FlagView(
            Guid.CreateVersion7(), flagKey, key, string.Empty, Now, Now,
            [.. EnvironmentKey.All.Select(environment =>
                new FlagStateView(environment, enabled.Contains(environment), [], Now))]));

        return flagKey;
    }

    private SegmentKey SeedSegment(string key, string plan = "pro")
    {
        var segmentKey = SegmentKey.Create(key).Value;

        _segments.Seed(new SegmentView(
            Guid.CreateVersion7(), segmentKey, key, string.Empty,
            SegmentDefinition.Create(
                [], [], [SegmentCondition.Create("plan", "equals", [AttributeValue.OfText(plan)]).Value]).Value,
            Now, Now));

        return segmentKey;
    }

    private async Task<CachedRuleset> GetAsync(EnvironmentKey environment) =>
        await CreateSut().GetAsync(environment, TestContext.Current.CancellationToken);

    [Fact]
    public async Task GetAsync_ShouldCarryTargetingForTheEnvironmentAsked()
    {
        var flag = SeedFlag("new-checkout", EnvironmentKey.Production);
        var segment = SeedSegment("beta-testers");
        _flags.SetTargeting(flag, EnvironmentKey.Production, [segment], Now);

        var production = await GetAsync(EnvironmentKey.Production);
        var development = await GetAsync(EnvironmentKey.Development);

        Assert.Equal(["beta-testers"], production.Ruleset.Flags.Single().TargetedSegments);
        Assert.Empty(development.Ruleset.Flags.Single().TargetedSegments);
    }

    [Fact]
    public async Task GetAsync_ShouldShipOnlyTheSegmentsSomeFlagInThisEnvironmentReaches()
    {
        // Not an optimisation: a key scoped to one environment has no business learning what
        // "internal-staff" is defined as when nothing it can evaluate points at it.
        var flag = SeedFlag("new-checkout", EnvironmentKey.Production);
        var beta = SeedSegment("beta-testers");
        SeedSegment("internal-staff");
        _flags.SetTargeting(flag, EnvironmentKey.Production, [beta], Now);

        var ruleset = (await GetAsync(EnvironmentKey.Production)).Ruleset;

        Assert.Equal(["beta-testers"], ruleset.Segments.Select(segment => segment.Key));
    }

    [Fact]
    public async Task GetAsync_ForAnEnvironmentWithNoTargeting_ShouldShipNoSegmentsAtAll()
    {
        var flag = SeedFlag("new-checkout", EnvironmentKey.Production);
        var beta = SeedSegment("beta-testers");
        _flags.SetTargeting(flag, EnvironmentKey.Production, [beta], Now);

        Assert.Empty((await GetAsync(EnvironmentKey.Development)).Ruleset.Segments);
    }

    [Fact]
    public async Task GetAsync_ShouldOrderFlagsAndSegmentsSoTheTagIsStable()
    {
        var second = SeedFlag("zzz", EnvironmentKey.Production);
        var first = SeedFlag("aaa", EnvironmentKey.Production);
        var beta = SeedSegment("zebra");
        var alpha = SeedSegment("alpha");
        _flags.SetTargeting(first, EnvironmentKey.Production, [beta, alpha], Now);
        _flags.SetTargeting(second, EnvironmentKey.Production, [alpha], Now);

        var ruleset = (await GetAsync(EnvironmentKey.Production)).Ruleset;

        Assert.Equal(["aaa", "zzz"], ruleset.Flags.Select(flag => flag.Key));
        Assert.Equal(["alpha", "zebra"], ruleset.Segments.Select(segment => segment.Key));
        Assert.Equal(["alpha", "zebra"], ruleset.Flags[0].TargetedSegments);
    }

    [Fact]
    public async Task GetAsync_WhenATargetedSegmentsDefinitionChanges_ShouldChangeTheTag()
    {
        var flag = SeedFlag("new-checkout", EnvironmentKey.Production);
        var segment = SeedSegment("beta-testers", plan: "pro");
        _flags.SetTargeting(flag, EnvironmentKey.Production, [segment], Now);

        var before = await GetAsync(EnvironmentKey.Production);

        SeedSegment("beta-testers", plan: "team");

        Assert.NotEqual(before.ETag, (await GetAsync(EnvironmentKey.Production)).ETag);
    }

    [Fact]
    public async Task GetAsync_WhenAnUntargetedSegmentChanges_ShouldNotChangeTheTag()
    {
        // The payoff of shipping only what is reachable: editing a segment nothing here points at
        // does not make every client in this environment refetch.
        var flag = SeedFlag("new-checkout", EnvironmentKey.Production);
        var beta = SeedSegment("beta-testers");
        SeedSegment("internal-staff", plan: "pro");
        _flags.SetTargeting(flag, EnvironmentKey.Production, [beta], Now);

        var before = await GetAsync(EnvironmentKey.Production);

        SeedSegment("internal-staff", plan: "team");

        Assert.Equal(before.ETag, (await GetAsync(EnvironmentKey.Production)).ETag);
    }

    [Fact]
    public async Task GetAsync_WhenOnlyTheTargetingChanges_ShouldStillChangeTheTag()
    {
        // The booleans are identical either way — the flag is on in both — so a tag computed over
        // the answers alone would tell a client nothing had changed while who it reaches had.
        var flag = SeedFlag("new-checkout", EnvironmentKey.Production);
        var segment = SeedSegment("beta-testers");

        var before = await GetAsync(EnvironmentKey.Production);

        _flags.SetTargeting(flag, EnvironmentKey.Production, [segment], Now.AddHours(1));

        Assert.NotEqual(before.ETag, (await GetAsync(EnvironmentKey.Production)).ETag);
    }

    [Fact]
    public async Task GetAsync_ForDifferentEnvironments_ShouldNotShareATag()
    {
        SeedFlag("new-checkout");

        var development = await GetAsync(EnvironmentKey.Development);
        var production = await GetAsync(EnvironmentKey.Production);

        Assert.NotEqual(development.ETag, production.ETag);
    }

    [Fact]
    public void Build_ShouldNotLetTwoDifferentRulesetsShareATag()
    {
        // The reason every part of the fingerprint is length-prefixed rather than newline-separated.
        // A condition value is arbitrary text and may contain a newline, so one value of "x\ns:y"
        // and two values of "x" and "y" render as identical bytes under a delimiter alone — and a
        // client would then be told nothing had changed when the segment's membership had.
        static SegmentView Segment(params string[] values) => new(
            Guid.CreateVersion7(), SegmentKey.Create("s").Value, "s", string.Empty,
            SegmentDefinition.Create([], [],
                [SegmentCondition.Create("plan", "one-of", [.. values.Select(AttributeValue.OfText)]).Value]).Value,
            Now, Now);

        var flags = new List<FlagView>
        {
            new(Guid.CreateVersion7(), FlagKey.Create("f").Value, "f", string.Empty, Now, Now,
                [new FlagStateView(EnvironmentKey.Production, true, [SegmentKey.Create("s").Value], Now)]),
        };

        var oneValue = RulesetProvider.Build(flags, [Segment("x\ns:y")], EnvironmentKey.Production);
        var twoValues = RulesetProvider.Build(flags, [Segment("x", "y")], EnvironmentKey.Production);

        Assert.NotEqual(oneValue.ETag, twoValues.ETag);
    }

    [Fact]
    public void ACachedRuleset_ShouldRoundTripThroughPlainSystemTextJson()
    {
        // HybridCache serializes with its own default options, not with RulesetJson's. If an
        // AttributeValue could not survive that, every test above would still pass — they run on
        // the in-memory tier, which stores the object — and Redis would fail in production. This is
        // the test that stands in for the tier those tests never reach.
        var segments = new List<SegmentView>
        {
            new(Guid.CreateVersion7(), SegmentKey.Create("beta-testers").Value, "Beta", string.Empty,
                SegmentDefinition.Create(["user-1"], ["user-2"],
                [
                    SegmentCondition.Create("plan", "one-of", [AttributeValue.OfText("pro")]).Value,
                    SegmentCondition.Create("seats", "greater-than", [AttributeValue.OfNumber(10.5)]).Value,
                    SegmentCondition.Create("internal", "equals", [AttributeValue.OfBoolean(true)]).Value,
                ]).Value,
                Now, Now),
        };

        var flags = new List<FlagView>
        {
            new(Guid.CreateVersion7(), FlagKey.Create("new-checkout").Value, "f", string.Empty, Now, Now,
                [new FlagStateView(EnvironmentKey.Production, true, [SegmentKey.Create("beta-testers").Value], Now)]),
        };

        var built = RulesetProvider.Build(flags, segments, EnvironmentKey.Production);

        var json = JsonSerializer.Serialize(built);
        var revived = JsonSerializer.Deserialize<CachedRuleset>(json)!;

        Assert.Equal(built.ETag, revived.ETag);
        Assert.Equal(
            built.Ruleset.Segments.Single().Conditions.Select(condition => condition.Values),
            revived.Ruleset.Segments.Single().Conditions.Select(condition => condition.Values));
        Assert.Equal(built.ETag, RulesetProvider.Build(flags, segments, EnvironmentKey.Production).ETag);
    }
}
