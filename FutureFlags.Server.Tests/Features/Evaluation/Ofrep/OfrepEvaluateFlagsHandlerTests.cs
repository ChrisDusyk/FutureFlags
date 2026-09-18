using FutureFlags.Domain.Environments;
using FutureFlags.Domain.Flags;
using FutureFlags.Domain.Segments;
using FutureFlags.Evaluation;
using FutureFlags.Server.Evaluation;
using FutureFlags.Server.Features.Evaluation.OfrepEvaluateFlag;
using FutureFlags.Server.Features.Evaluation.OfrepEvaluateFlags;
using FutureFlags.Server.Tests.Fakes;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace FutureFlags.Server.Tests.Features.Evaluation.Ofrep;

/// <summary>
/// The OpenFeature Remote Evaluation Protocol routes: the same answers every other evaluation route
/// gives, in the shape a vendor-neutral client already knows how to read.
/// </summary>
public class OfrepEvaluateFlagsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeFlagViewRepository _flags = new();
    private readonly FakeSegmentViewRepository _segments = new();

    private RulesetProvider CreateProvider() => new(
        _flags,
        _segments,
        new ServiceCollection().AddHybridCache().Services
            .BuildServiceProvider()
            .GetRequiredService<HybridCache>());

    private void SeedFlag(string key, bool enabled = true, params string[] targeted)
    {
        var flagKey = FlagKey.Create(key).Value;

        _flags.Seed(new FlagView(
            Guid.CreateVersion7(), flagKey, key, string.Empty, Now, Now,
            [.. EnvironmentKey.All.Select(environment => new FlagStateView(
                environment,
                enabled,
                [.. targeted.Select(segment => SegmentKey.Create(segment).Value)],
                Now))]));
    }

    private void SeedSegment(string key, SegmentDefinition definition) =>
        _segments.Seed(new SegmentView(
            Guid.CreateVersion7(), SegmentKey.Create(key).Value, key, string.Empty, definition, Now, Now));

    private async Task<OfrepEvaluatedFlags> BulkAsync(FlagContext context) =>
        (await new OfrepEvaluateFlagsHandler(CreateProvider()).HandleAsync(
            new OfrepEvaluateFlagsQuery(EnvironmentKey.Production, context),
            TestContext.Current.CancellationToken)).Value;

    [Fact]
    public async Task Bulk_ShouldCarryAValueVariantAndReasonForEveryFlag()
    {
        SeedFlag("dark-mode");
        SeedFlag("new-checkout", enabled: false);

        var evaluated = await BulkAsync(FlagContext.Empty);

        var dark = evaluated.Response.Flags.Single(flag => flag.Key == "dark-mode");
        Assert.Equal(FlagValue.True, dark.Value);
        Assert.Equal(FlagVariantNames.On, dark.Variant);
        Assert.Equal(EvaluationReason.Static, dark.Reason);

        var checkout = evaluated.Response.Flags.Single(flag => flag.Key == "new-checkout");
        Assert.Equal(FlagValue.False, checkout.Value);
        Assert.Equal(FlagVariantNames.Off, checkout.Variant);
        Assert.Equal(EvaluationReason.Disabled, checkout.Reason);
    }

    [Fact]
    public async Task Bulk_ShouldReportTargetingMatchAndDefaultRatherThanAnError()
    {
        // The reason mapping that matters most: a targeted flag that matched nothing is DEFAULT,
        // not an error, so nothing alerting on error reasons sees a narrowed flag as an outage.
        SeedSegment("beta", SegmentDefinition.Create(["user-17"], null, null).Value);
        SeedFlag("new-checkout", enabled: true, "beta");

        var matched = await BulkAsync(FlagContext.For("user-17"));
        var unmatched = await BulkAsync(FlagContext.For("user-99"));

        Assert.Equal(EvaluationReason.TargetingMatch, matched.Response.Flags.Single().Reason);
        Assert.Equal(FlagValue.True, matched.Response.Flags.Single().Value);

        Assert.Equal(EvaluationReason.Default, unmatched.Response.Flags.Single().Reason);
        Assert.Equal(FlagValue.False, unmatched.Response.Flags.Single().Value);
    }

    [Fact]
    public async Task Bulk_ShouldOrderFlagsByKey()
    {
        SeedFlag("zulu");
        SeedFlag("alpha");

        var evaluated = await BulkAsync(FlagContext.Empty);

        Assert.Equal(["alpha", "zulu"], evaluated.Response.Flags.Select(flag => flag.Key));
    }

    [Fact]
    public async Task Bulk_ShouldCarryAnEmptyMetadataRecordRatherThanNothing()
    {
        // The specification requires an empty record, not an absent field, so a consumer can read
        // it without a guard.
        SeedFlag("dark-mode");

        var evaluated = await BulkAsync(FlagContext.Empty);

        Assert.NotNull(evaluated.Response.Metadata);
        Assert.Empty(evaluated.Response.Metadata);
        Assert.Empty(evaluated.Response.Flags.Single().Metadata);
    }

    [Fact]
    public async Task Bulk_ShouldGiveADifferentTagForADifferentContext()
    {
        // Otherwise a client that changed its context and reused its tag would be told nothing had
        // changed. This is what makes the 304 on this route honest.
        SeedSegment("beta", SegmentDefinition.Create(["user-17"], null, null).Value);
        SeedFlag("new-checkout", enabled: true, "beta");

        var one = await BulkAsync(FlagContext.For("user-17"));
        var two = await BulkAsync(FlagContext.For("user-99"));

        Assert.NotEqual(one.ETag, two.ETag);
    }

    [Fact]
    public async Task Bulk_ShouldGiveTheSameTagForTheSameQuestion()
    {
        SeedFlag("dark-mode");

        var one = await BulkAsync(FlagContext.For("user-17"));
        var two = await BulkAsync(FlagContext.For("user-17"));

        Assert.Equal(one.ETag, two.ETag);
    }

    [Fact]
    public async Task Single_ShouldResolveOneFlag()
    {
        SeedFlag("dark-mode");

        var result = await new OfrepEvaluateFlagHandler(CreateProvider()).HandleAsync(
            new OfrepEvaluateFlagQuery(
                EnvironmentKey.Production, FlagKey.Create("dark-mode").Value, FlagContext.Empty),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("dark-mode", result.Value.Key);
        Assert.Equal(FlagValue.True, result.Value.Value);
        Assert.Equal(EvaluationReason.Static, result.Value.Reason);
    }

    [Fact]
    public async Task Single_WithAKeyTheEnvironmentDoesNotCarry_ShouldBeNotFound()
    {
        // The one place this platform's "an unknown key is simply off" rule does not hold. A
        // provider reads FLAG_NOT_FOUND and returns the caller's own default, which is what an
        // application asking for a flag it believes exists actually wants — answering false would
        // look like a deliberate off.
        SeedFlag("dark-mode");

        var result = await new OfrepEvaluateFlagHandler(CreateProvider()).HandleAsync(
            new OfrepEvaluateFlagQuery(
                EnvironmentKey.Production, FlagKey.Create("never-defined").Value, FlagContext.Empty),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(EvaluationErrorCode.FlagNotFound, result.Error.Code);
        Assert.Equal(Domain.Shared.ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task BothRoutes_ShouldAgreeWithEachOtherAndWithTheBooleanSurface()
    {
        // Three routes reading one cached ruleset is the whole reason RulesetProvider is shared.
        // An OFREP client and a native one asking at the same moment must not be told different
        // things.
        SeedSegment("beta", SegmentDefinition.Create(["user-17"], null, null).Value);
        SeedFlag("new-checkout", enabled: true, "beta");
        SeedFlag("dark-mode");

        var context = FlagContext.For("user-17");
        var provider = CreateProvider();

        var bulk = (await new OfrepEvaluateFlagsHandler(provider).HandleAsync(
            new OfrepEvaluateFlagsQuery(EnvironmentKey.Production, context),
            TestContext.Current.CancellationToken)).Value;

        foreach (var flag in bulk.Response.Flags)
        {
            var single = (await new OfrepEvaluateFlagHandler(provider).HandleAsync(
                new OfrepEvaluateFlagQuery(
                    EnvironmentKey.Production, FlagKey.Create(flag.Key).Value, context),
                TestContext.Current.CancellationToken)).Value;

            Assert.Equal(flag.Value, single.Value);
            Assert.Equal(flag.Variant, single.Variant);
            Assert.Equal(flag.Reason, single.Reason);
        }
    }
}
