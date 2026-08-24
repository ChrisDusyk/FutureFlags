using FeatureFlags.Domain.Environments;
using FeatureFlags.Domain.Flags;
using FeatureFlags.Domain.Segments;
using FeatureFlags.Server.Features.Segments.DeleteSegment;
using FeatureFlags.Server.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace FeatureFlags.Server.Tests.Features.Segments.DeleteSegment;

public class DeleteSegmentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly SegmentKey Key = SegmentKey.Create("beta-testers").Value;

    private static (DeleteSegmentHandler Handler, FakeSegmentRepository Segments, FakeFlagViewRepository Flags) Build()
    {
        var segments = new FakeSegmentRepository();
        var flags = new FakeFlagViewRepository();

        segments.Seed(Segment.Create(Key.Value, "Beta testers", null, null, Now, Actor).Value);

        return (new DeleteSegmentHandler(segments, flags, new FakeTimeProvider(Now)), segments, flags);
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
    public async Task HandleAsync_WhenNothingTargetsIt_ShouldRetireIt()
    {
        var (handler, segments, _) = Build();

        var result = await handler.HandleAsync(new DeleteSegmentCommand(Key, Actor), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(segments.Committed.Single().IsDeleted);
        Assert.Equal(1, segments.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WhenAFlagTargetsIt_ShouldRefuseAndNameTheFlag()
    {
        var (handler, segments, flags) = Build();
        flags.Seed(Flag("new-checkout", "New checkout"));
        flags.SetTargeting(FlagKey.Create("new-checkout").Value, EnvironmentKey.Production, [Key], Now);

        var result = await handler.HandleAsync(new DeleteSegmentCommand(Key, Actor), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("Segment.StillTargeted", result.Error.Code);
        // The next thing whoever hit this has to do is go and untarget it, so the message says where.
        Assert.Contains("new-checkout", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("prod", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(0, segments.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_ShouldRefuseWhenTheTargetingIsInAnEnvironmentTheCallerIsNotLookingAt()
    {
        // A segment still holding up production is not deletable because development stopped
        // needing it. There is no environment parameter here at all, on purpose.
        var (handler, _, flags) = Build();
        flags.Seed(Flag("new-checkout", "New checkout"));
        flags.SetTargeting(FlagKey.Create("new-checkout").Value, EnvironmentKey.Staging, [Key], Now);

        var result = await handler.HandleAsync(new DeleteSegmentCommand(Key, Actor), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Contains("stg", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_WhenAnotherSegmentIsTargeted_ShouldStillAllowThisOne()
    {
        var (handler, segments, flags) = Build();
        flags.Seed(Flag("new-checkout", "New checkout"));
        flags.SetTargeting(
            FlagKey.Create("new-checkout").Value,
            EnvironmentKey.Production,
            [SegmentKey.Create("internal-staff").Value],
            Now);

        var result = await handler.HandleAsync(new DeleteSegmentCommand(Key, Actor), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(segments.Committed.Single().IsDeleted);
    }

    [Fact]
    public async Task HandleAsync_ForASegmentThatIsNotThere_ShouldReportNotFound()
    {
        var (handler, _, _) = Build();
        var missing = SegmentKey.Create("never-existed").Value;

        var result = await handler.HandleAsync(new DeleteSegmentCommand(missing, Actor), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.NotFound(missing), result.Error);
    }

    [Fact]
    public async Task HandleAsync_Twice_ShouldReportItIsAlreadyGone()
    {
        var (handler, _, _) = Build();
        await handler.HandleAsync(new DeleteSegmentCommand(Key, Actor), TestContext.Current.CancellationToken);

        var result = await handler.HandleAsync(new DeleteSegmentCommand(Key, Actor), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.AlreadyDeleted(Key), result.Error);
    }
}
