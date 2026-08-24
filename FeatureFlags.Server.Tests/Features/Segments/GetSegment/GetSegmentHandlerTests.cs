using FeatureFlags.Domain.Environments;
using FeatureFlags.Domain.Flags;
using FeatureFlags.Domain.Segments;
using FeatureFlags.Evaluation;
using FeatureFlags.Server.Features.Segments.GetSegment;
using FeatureFlags.Server.Tests.Fakes;

namespace FeatureFlags.Server.Tests.Features.Segments.GetSegment;

public class GetSegmentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly SegmentKey Key = SegmentKey.Create("beta-testers").Value;

    private static (GetSegmentHandler Handler, FakeSegmentViewRepository Segments, FakeFlagViewRepository Flags) Build()
    {
        var segments = new FakeSegmentViewRepository();
        var flags = new FakeFlagViewRepository();

        segments.Seed(new SegmentView(
            Guid.CreateVersion7(),
            Key,
            "Beta testers",
            "People trying the new thing.",
            SegmentDefinition.Create(
                ["user-17"], ["user-99"],
                [SegmentCondition.Create("plan", "one-of", [AttributeValue.OfText("pro")]).Value]).Value,
            Now,
            Now));

        return (new GetSegmentHandler(segments, flags), segments, flags);
    }

    private static FlagView Flag(string key, string name) => new(
        Guid.CreateVersion7(),
        FlagKey.Create(key).Value,
        name,
        string.Empty,
        Now,
        Now,
        [.. EnvironmentKey.All.Select(environment => new FlagStateView(environment, true, [], Now))]);

    [Fact]
    public async Task HandleAsync_ShouldReturnTheDefinition()
    {
        var (handler, _, _) = Build();

        var result = await handler.HandleAsync(new GetSegmentQuery(Key), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(["user-17"], result.Value.Definition.IncludedKeys);
        Assert.Equal(["user-99"], result.Value.Definition.ExcludedKeys);
        Assert.Equal("plan", result.Value.Definition.Conditions.Single().Attribute);
    }

    [Fact]
    public async Task HandleAsync_WithNothingTargetingIt_ShouldSaySoWithAnEmptyList()
    {
        var (handler, _, _) = Build();

        var result = await handler.HandleAsync(new GetSegmentQuery(Key), TestContext.Current.CancellationToken);

        Assert.Empty(result.Value.TargetedBy);
    }

    [Fact]
    public async Task HandleAsync_ShouldListEveryFlagAndEnvironmentThatTargetsIt()
    {
        // "See who depends on it, before you edit" — one entry per flag *and* environment, because
        // a segment can be holding up production while development has moved on.
        var (handler, _, flags) = Build();

        flags.Seed(Flag("new-checkout", "New checkout"));
        flags.Seed(Flag("fast-search", "Fast search"));
        flags.SetTargeting(FlagKey.Create("new-checkout").Value, EnvironmentKey.Development, [Key], Now);
        flags.SetTargeting(FlagKey.Create("new-checkout").Value, EnvironmentKey.Production, [Key], Now);
        flags.SetTargeting(FlagKey.Create("fast-search").Value, EnvironmentKey.Development, [Key], Now);

        var result = await handler.HandleAsync(new GetSegmentQuery(Key), TestContext.Current.CancellationToken);

        Assert.Collection(
            result.Value.TargetedBy,
            entry => Assert.Equal(("fast-search", "dev"), (entry.FlagKey, entry.Environment)),
            entry => Assert.Equal(("new-checkout", "dev"), (entry.FlagKey, entry.Environment)),
            entry => Assert.Equal(("new-checkout", "prod"), (entry.FlagKey, entry.Environment)));
    }

    [Fact]
    public async Task HandleAsync_ForASegmentThatIsNotThere_ShouldReportNotFound()
    {
        var (handler, _, _) = Build();
        var missing = SegmentKey.Create("never-existed").Value;

        var result = await handler.HandleAsync(new GetSegmentQuery(missing), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.NotFound(missing), result.Error);
    }
}
