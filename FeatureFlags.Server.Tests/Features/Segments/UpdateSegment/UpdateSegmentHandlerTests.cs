using FeatureFlags.Domain.Segments;
using FeatureFlags.Evaluation;
using FeatureFlags.Server.Features.Segments.UpdateSegment;
using FeatureFlags.Server.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace FeatureFlags.Server.Tests.Features.Segments.UpdateSegment;

public class UpdateSegmentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly SegmentKey Key = SegmentKey.Create("beta-testers").Value;

    private static SegmentDefinition Definition(string plan) => SegmentDefinition.Create(
        [], [], [SegmentCondition.Create("plan", "equals", [AttributeValue.OfText(plan)]).Value]).Value;

    private static (UpdateSegmentHandler Handler, FakeSegmentRepository Repository, FakeTimeProvider Clock) Build()
    {
        var repository = new FakeSegmentRepository();
        var clock = new FakeTimeProvider(Now);

        repository.Seed(Segment.Create(Key.Value, "Beta testers", "Original.", Definition("pro"), Now, Actor).Value);

        return (new UpdateSegmentHandler(repository, clock), repository, clock);
    }

    [Fact]
    public async Task HandleAsync_ShouldReplaceBothTheDetailsAndTheDefinition()
    {
        var (handler, _, clock) = Build();
        clock.Advance(TimeSpan.FromHours(1));

        var result = await handler.HandleAsync(
            new UpdateSegmentCommand(Key, "Early access", "Changed.", Definition("team"), Actor),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("Early access", result.Value.Name);
        Assert.Equal("Changed.", result.Value.Description);
        Assert.Equal([AttributeValue.OfText("team")], result.Value.Definition.Conditions.Single().Values);
        Assert.Equal(Now.AddHours(1), result.Value.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_WithNothingChanged_ShouldSucceedWithoutMovingTheTimestamp()
    {
        // The idempotence SegmentDefinition's normal form exists for: opening the editor and saving
        // must not churn every SDK's ETag.
        var (handler, _, clock) = Build();
        clock.Advance(TimeSpan.FromHours(1));

        var result = await handler.HandleAsync(
            new UpdateSegmentCommand(Key, "Beta testers", "Original.", Definition("pro"), Actor),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, result.Value.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_WithoutAName_ShouldFailBeforeTouchingTheDefinition()
    {
        var (handler, repository, _) = Build();

        var result = await handler.HandleAsync(
            new UpdateSegmentCommand(Key, "  ", null, Definition("team"), Actor),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.NameRequired, result.Error);
        Assert.Equal(0, repository.SaveChangesCallCount);
        // The definition must not have moved either — a half-applied edit is the thing one PUT
        // over both of them exists to prevent.
        Assert.Equal(Definition("pro"), repository.Committed.Single().Definition);
    }

    [Fact]
    public async Task HandleAsync_ForARetiredSegment_ShouldSaySoRatherThanReportNotFound()
    {
        var (handler, repository, _) = Build();
        repository.Committed.Single().Delete(Now, Actor);

        var result = await handler.HandleAsync(
            new UpdateSegmentCommand(Key, "Early access", null, Definition("team"), Actor),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.Deleted(Key), result.Error);
    }

    [Fact]
    public async Task HandleAsync_ForASegmentThatIsNotThere_ShouldReportNotFound()
    {
        var (handler, _, _) = Build();
        var missing = SegmentKey.Create("never-existed").Value;

        var result = await handler.HandleAsync(
            new UpdateSegmentCommand(missing, "Name", null, SegmentDefinition.Empty, Actor),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.NotFound(missing), result.Error);
    }
}
