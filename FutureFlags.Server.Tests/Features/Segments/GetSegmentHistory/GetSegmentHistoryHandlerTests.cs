using FutureFlags.Domain.Segments;
using FutureFlags.Domain.Segments.Events;
using FutureFlags.Server.Features.Segments.GetSegmentHistory;
using FutureFlags.Server.Tests.Fakes;

namespace FutureFlags.Server.Tests.Features.Segments.GetSegmentHistory;

public class GetSegmentHistoryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly SegmentKey Key = SegmentKey.Create("beta-testers").Value;

    private static (GetSegmentHistoryHandler Handler, FakeSegmentRepository Segments, FakeSegmentViewRepository Views)
        Build()
    {
        var segments = new FakeSegmentRepository();
        var views = new FakeSegmentViewRepository();
        var users = new FakeUserRepository();

        return (new GetSegmentHistoryHandler(segments, views, users), segments, views);
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnTheSegmentsHistory()
    {
        var (handler, segments, views) = Build();
        var segment = Segment.Create(Key.Value, "Beta testers", null, null, Now, Actor).Value;
        segments.Seed(segment);
        views.SeedHistory(segment.Id, new SegmentCreatedEvent(segment.Id, Key, "Beta testers", "", Now, Actor));

        var result = await handler.HandleAsync(new GetSegmentHistoryQuery(Key), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("SegmentCreated", result.Value.Entries.Single().EventType);
    }

    // The tombstone exists specifically so a retired segment's history stays reachable — resolving
    // the key through the filtered view side instead of the write side would defeat that.
    [Fact]
    public async Task HandleAsync_ForARetiredSegment_ShouldStillReturnItsHistory()
    {
        var (handler, segments, views) = Build();
        var segment = Segment.Create(Key.Value, "Beta testers", null, null, Now, Actor).Value;
        segment.Delete(Now, Actor);
        segments.Seed(segment);
        views.SeedHistory(
            segment.Id,
            new SegmentDeletedEvent(segment.Id, Now, Actor),
            new SegmentCreatedEvent(segment.Id, Key, "Beta testers", "", Now, Actor));

        var result = await handler.HandleAsync(new GetSegmentHistoryQuery(Key), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Entries.Count);
        Assert.Equal("SegmentDeleted", result.Value.Entries[0].EventType);
    }

    [Fact]
    public async Task HandleAsync_ForASegmentThatIsNotThere_ShouldReportNotFound()
    {
        var (handler, _, _) = Build();
        var missing = SegmentKey.Create("never-existed").Value;

        var result = await handler.HandleAsync(new GetSegmentHistoryQuery(missing), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.NotFound(missing), result.Error);
    }
}
