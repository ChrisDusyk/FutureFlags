using FeatureFlags.Domain.Segments;
using FeatureFlags.Domain.Segments.Events;
using FeatureFlags.Evaluation;

namespace FeatureFlags.Domain.Tests.Segments;

public class SegmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static SegmentDefinition Definition(string plan = "pro") =>
        SegmentDefinition.Create(
            [],
            [],
            [SegmentCondition.Create("plan", "equals", [AttributeValue.OfText(plan)]).Value]).Value;

    private static Segment Created(SegmentDefinition? definition = null) =>
        Segment.Create("beta-testers", "Beta testers", "People trying the new thing.", definition, Now, Actor).Value;

    [Fact]
    public void Create_ShouldRaiseACreatedEventAndADefinitionEvent()
    {
        var segment = Created(Definition());

        Assert.Collection(
            segment.UncommittedEvents,
            @event => Assert.IsType<SegmentCreatedEvent>(@event),
            @event => Assert.IsType<SegmentDefinitionChangedEvent>(@event));
        Assert.Equal(2, segment.Version);
    }

    [Fact]
    public void Create_WithoutADefinition_ShouldStartEmptyRatherThanMatchingEveryone()
    {
        var segment = Created(definition: null);

        Assert.Equal(SegmentDefinition.Empty, segment.Definition);
        Assert.True(segment.Definition.IsEmpty);
    }

    [Fact]
    public void Create_ShouldNormalizeTheKeyAndTrimTheDetails()
    {
        var segment = Segment.Create("  Beta-Testers ", "  Beta testers  ", "  Trying it.  ", null, Now, Actor).Value;

        Assert.Equal("beta-testers", segment.Key.Value);
        Assert.Equal("Beta testers", segment.Name);
        Assert.Equal("Trying it.", segment.Description);
    }

    [Fact]
    public void Create_WithoutAName_ShouldFail()
    {
        var result = Segment.Create("beta-testers", "  ", null, null, Now, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.NameRequired, result.Error);
    }

    [Fact]
    public void Create_WithABadKey_ShouldFailOnTheKeyRatherThanTheName()
    {
        var result = Segment.Create("beta testers", "Beta testers", null, null, Now, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.KeyInvalidFormat, result.Error);
    }

    [Fact]
    public void UpdateDetails_WithTheSameValues_ShouldRaiseNothing()
    {
        var segment = Created();
        var eventCountAfterCreate = segment.UncommittedEvents.Count;

        var result = segment.UpdateDetails("Beta testers", "People trying the new thing.", Now.AddHours(1), Actor);

        Assert.True(result.IsSuccess);
        Assert.Equal(eventCountAfterCreate, segment.UncommittedEvents.Count);
        Assert.Equal(Now, segment.UpdatedAt);
    }

    [Fact]
    public void UpdateDetails_WithANewName_ShouldRaiseAndMoveTheTimestamp()
    {
        var segment = Created();
        var eventCountAfterCreate = segment.UncommittedEvents.Count;

        var result = segment.UpdateDetails("Early access", "People trying the new thing.", Now.AddHours(1), Actor);

        Assert.True(result.IsSuccess);
        Assert.Equal(eventCountAfterCreate + 1, segment.UncommittedEvents.Count);
        Assert.IsType<SegmentDetailsChangedEvent>(segment.UncommittedEvents[^1]);
        Assert.Equal("Early access", segment.Name);
        Assert.Equal(Now.AddHours(1), segment.UpdatedAt);
    }

    [Fact]
    public void ChangeDefinition_WithAnEquivalentDefinition_ShouldRaiseNothing()
    {
        // The idempotence that SegmentDefinition's normal form exists to make possible: the console
        // posting the editor back unchanged must not churn every SDK's ETag.
        var segment = Created(Definition());
        var eventCountAfterCreate = segment.UncommittedEvents.Count;

        var rebuilt = SegmentDefinition.Create(
            [],
            [],
            [SegmentCondition.Create("PLAN", "equals", [AttributeValue.OfText("pro")]).Value]).Value;

        var result = segment.ChangeDefinition(rebuilt, Now.AddHours(1), Actor);

        Assert.True(result.IsSuccess);
        Assert.Equal(eventCountAfterCreate, segment.UncommittedEvents.Count);
        Assert.Equal(Now, segment.UpdatedAt);
    }

    [Fact]
    public void ChangeDefinition_WithADifferentDefinition_ShouldRaise()
    {
        var segment = Created(Definition());
        var eventCountAfterCreate = segment.UncommittedEvents.Count;

        var result = segment.ChangeDefinition(Definition("team"), Now.AddHours(1), Actor);

        Assert.True(result.IsSuccess);
        Assert.Equal(eventCountAfterCreate + 1, segment.UncommittedEvents.Count);
        Assert.IsType<SegmentDefinitionChangedEvent>(segment.UncommittedEvents[^1]);
        Assert.Equal(Definition("team"), segment.Definition);
        Assert.Equal(Now.AddHours(1), segment.UpdatedAt);
    }

    [Fact]
    public void Delete_ShouldTombstoneRatherThanForget()
    {
        var segment = Created();

        var result = segment.Delete(Now.AddHours(1), Actor);

        Assert.True(result.IsSuccess);
        Assert.True(segment.IsDeleted);
        Assert.Equal(Now.AddHours(1), segment.DeletedAt.Reduce(default(DateTimeOffset)));
        // Everything about it is still readable, which is the point.
        Assert.Equal("beta-testers", segment.Key.Value);
    }

    [Fact]
    public void Delete_Twice_ShouldFail()
    {
        var segment = Created();
        segment.Delete(Now.AddHours(1), Actor);

        var result = segment.Delete(Now.AddHours(2), Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.AlreadyDeleted(segment.Key), result.Error);
    }

    [Fact]
    public void ADeletedSegment_ShouldRefuseFurtherChanges()
    {
        var segment = Created();
        segment.Delete(Now.AddHours(1), Actor);

        Assert.Equal(SegmentErrors.Deleted(segment.Key), segment.UpdateDetails("x", null, Now, Actor).Error);
        Assert.Equal(SegmentErrors.Deleted(segment.Key), segment.ChangeDefinition(Definition("team"), Now, Actor).Error);
    }

    [Fact]
    public void Rehydrate_ShouldReproduceTheSameSegment()
    {
        var original = Created(Definition());
        original.UpdateDetails("Early access", "Changed.", Now.AddHours(1), Actor);
        original.ChangeDefinition(Definition("team"), Now.AddHours(2), Actor);

        var replayed = Segment.Rehydrate(original.Id, original.UncommittedEvents);

        Assert.Equal(original.Key, replayed.Key);
        Assert.Equal(original.Name, replayed.Name);
        Assert.Equal(original.Description, replayed.Description);
        Assert.Equal(original.Definition, replayed.Definition);
        Assert.Equal(original.CreatedAt, replayed.CreatedAt);
        Assert.Equal(original.UpdatedAt, replayed.UpdatedAt);
        Assert.Equal(original.Version, replayed.Version);
        // Replaying history is not the same as making it.
        Assert.Empty(replayed.UncommittedEvents);
    }

    [Fact]
    public void Rehydrate_ShouldCarryATombstoneAcross()
    {
        var original = Created();
        original.Delete(Now.AddHours(1), Actor);

        var replayed = Segment.Rehydrate(original.Id, original.UncommittedEvents);

        Assert.True(replayed.IsDeleted);
    }

    [Fact]
    public void Rehydrate_WithAnotherSegmentsEvent_ShouldThrow()
    {
        var original = Created();

        Assert.Throws<InvalidOperationException>(() => Segment.Rehydrate(Guid.NewGuid(), original.UncommittedEvents));
    }

    [Fact]
    public void Rehydrate_WithNoCreatedEvent_ShouldThrow()
    {
        var id = Guid.CreateVersion7();

        Assert.Throws<InvalidOperationException>(() =>
            Segment.Rehydrate(id, [new SegmentDetailsChangedEvent(id, "x", "y", Now, Actor)]));
    }

    [Fact]
    public void Rehydrate_WithAnEventTypeThisBuildDoesNotKnow_ShouldThrowRatherThanSkipIt()
    {
        // Folding it silently would advance Version past a dropped change and hand back an
        // aggregate that looks consistent and is not.
        var original = Created();
        var unknown = new UnknownSegmentEvent(original.Id, Now, Actor);

        Assert.Throws<InvalidOperationException>(() =>
            Segment.Rehydrate(original.Id, [.. original.UncommittedEvents, unknown]));
    }

    private sealed record UnknownSegmentEvent(Guid SegmentId, DateTimeOffset OccurredAt, Guid? CausedBy) : ISegmentEvent;
}
