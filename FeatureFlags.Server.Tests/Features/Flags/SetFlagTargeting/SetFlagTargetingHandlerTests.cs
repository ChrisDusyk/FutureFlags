using FeatureFlags.Domain.Environments;
using FeatureFlags.Domain.Flags;
using FeatureFlags.Domain.Segments;
using FeatureFlags.Server.Features.Flags.SetFlagTargeting;
using FeatureFlags.Server.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace FeatureFlags.Server.Tests.Features.Flags.SetFlagTargeting;

public class SetFlagTargetingHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly FlagKey Key = FlagKey.Create("new-checkout").Value;

    private static SegmentKey Segment(string value) => SegmentKey.Create(value).Value;

    private static (SetFlagTargetingHandler Handler, FakeFeatureFlagRepository Flags, FakeSegmentViewRepository Segments) Build(
        params string[] existingSegments)
    {
        var flags = new FakeFeatureFlagRepository();
        var segments = new FakeSegmentViewRepository();

        flags.Seed(FeatureFlag.Create(Key.Value, "New checkout", null, EnvironmentKey.All, Now, Actor).Value);

        foreach (var segment in existingSegments)
            segments.Seed(Segment(segment));

        return (new SetFlagTargetingHandler(flags, segments, new FakeTimeProvider(Now)), flags, segments);
    }

    [Fact]
    public async Task HandleAsync_ShouldTargetTheSegmentsAndPersistOnce()
    {
        var (handler, flags, _) = Build("beta-testers", "internal-staff");

        var result = await handler.HandleAsync(
            new SetFlagTargetingCommand(Key, EnvironmentKey.Production, [Segment("internal-staff"), Segment("beta-testers")], Actor),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("prod", result.Value.Environment);
        // Normalized on the way through, so the answer is the set rather than the argument's order.
        Assert.Equal(["beta-testers", "internal-staff"], result.Value.TargetedSegments);
        Assert.Equal(1, flags.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_ShouldReportWhetherTheFlagIsOnAsWellAsWhoItReaches()
    {
        // A flag that is off reaches nobody whatever it targets, so a response carrying only the
        // targeting would let a screen claim something the evaluator would not agree with.
        var (handler, flags, _) = Build("beta-testers");
        flags.Committed.Single().SetEnabled(EnvironmentKey.Production, isEnabled: false, Now, Actor);

        var result = await handler.HandleAsync(
            new SetFlagTargetingCommand(Key, EnvironmentKey.Production, [Segment("beta-testers")], Actor),
            TestContext.Current.CancellationToken);

        Assert.False(result.Value.IsEnabled);
        Assert.Equal(["beta-testers"], result.Value.TargetedSegments);
    }

    [Fact]
    public async Task HandleAsync_WithASegmentThatDoesNotExist_ShouldRefuseAndNameIt()
    {
        // Caught here rather than left to the evaluator's "unknown means no match": a typo is
        // something somebody can still fix, and the alternative is a flag that reaches nobody and
        // never says why.
        var (handler, flags, _) = Build("beta-testers");

        var result = await handler.HandleAsync(
            new SetFlagTargetingCommand(Key, EnvironmentKey.Production, [Segment("beta-testers"), Segment("beta-testrs")], Actor),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("Segment.Unknown", result.Error.Code);
        Assert.Contains("beta-testrs", result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'beta-testers'", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(0, flags.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WithNoSegments_ShouldGoBackToReachingEveryone()
    {
        var (handler, _, _) = Build("beta-testers");
        await handler.HandleAsync(
            new SetFlagTargetingCommand(Key, EnvironmentKey.Production, [Segment("beta-testers")], Actor),
            TestContext.Current.CancellationToken);

        var result = await handler.HandleAsync(
            new SetFlagTargetingCommand(Key, EnvironmentKey.Production, [], Actor),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.TargetedSegments);
    }

    [Fact]
    public async Task HandleAsync_ShouldLeaveTheOtherEnvironmentsAlone()
    {
        var (handler, flags, _) = Build("beta-testers");

        await handler.HandleAsync(
            new SetFlagTargetingCommand(Key, EnvironmentKey.Production, [Segment("beta-testers")], Actor),
            TestContext.Current.CancellationToken);

        var flag = flags.Committed.Single();
        Assert.Empty(flag.StateIn(EnvironmentKey.Development).Reduce(() => null!).TargetedSegments);
        Assert.Empty(flag.StateIn(EnvironmentKey.Staging).Reduce(() => null!).TargetedSegments);
    }

    [Fact]
    public async Task HandleAsync_ForAFlagThatIsNotThere_ShouldReportNotFound()
    {
        var (handler, _, _) = Build("beta-testers");
        var missing = FlagKey.Create("never-existed").Value;

        var result = await handler.HandleAsync(
            new SetFlagTargetingCommand(missing, EnvironmentKey.Production, [], Actor),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(FlagErrors.NotFound(missing), result.Error);
    }

    [Fact]
    public async Task HandleAsync_WithASegmentThatWasNeverSeeded_ShouldRefuseIt()
    {
        var (handler, _, _) = Build();

        var result = await handler.HandleAsync(
            new SetFlagTargetingCommand(Key, EnvironmentKey.Production, [Segment("beta-testers")], Actor),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("Segment.Unknown", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_WithARetiredSegment_ShouldRefuseIt()
    {
        // Seeded and then retired, not simply never seeded — a segment that once existed and a key
        // nobody ever used should be indistinguishable from the read side, and only actually
        // retiring one proves that rather than assuming it.
        var (handler, _, segments) = Build("beta-testers");
        segments.Retire(Segment("beta-testers"));

        var result = await handler.HandleAsync(
            new SetFlagTargetingCommand(Key, EnvironmentKey.Production, [Segment("beta-testers")], Actor),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("Segment.Unknown", result.Error.Code);
    }
}
