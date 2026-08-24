using FeatureFlags.Domain.Segments;
using FeatureFlags.Evaluation;
using FeatureFlags.Server.Features.Segments.CreateSegment;
using FeatureFlags.Server.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace FeatureFlags.Server.Tests.Features.Segments.CreateSegment;

public class CreateSegmentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static SegmentDefinition Definition() => SegmentDefinition.Create(
        ["user-17"],
        [],
        [SegmentCondition.Create("plan", "one-of", [AttributeValue.OfText("pro")]).Value]).Value;

    private static (CreateSegmentHandler Handler, FakeSegmentRepository Repository) Build()
    {
        var repository = new FakeSegmentRepository();

        return (new CreateSegmentHandler(repository, new FakeTimeProvider(Now)), repository);
    }

    [Fact]
    public async Task HandleAsync_ShouldCreateAndPersistOnce()
    {
        var (handler, repository) = Build();

        var result = await handler.HandleAsync(
            new CreateSegmentCommand("Beta-Testers", "  Beta testers  ", null, Definition(), Actor),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("beta-testers", result.Value.Key);
        Assert.Equal("Beta testers", result.Value.Name);
        Assert.Equal(Now, result.Value.CreatedAt);
        Assert.Equal(1, repository.SaveChangesCallCount);
        Assert.Single(repository.Committed);
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnTheDefinitionItStored()
    {
        var (handler, _) = Build();

        var result = await handler.HandleAsync(
            new CreateSegmentCommand("beta-testers", "Beta testers", null, Definition(), Actor),
            TestContext.Current.CancellationToken);

        Assert.Equal(["user-17"], result.Value.Definition.IncludedKeys);
        var condition = Assert.Single(result.Value.Definition.Conditions);
        Assert.Equal("plan", condition.Attribute);
        Assert.Equal("one-of", condition.Operator);
        Assert.Equal([AttributeValue.OfText("pro")], condition.Values);
    }

    [Fact]
    public async Task HandleAsync_WithAnInvalidKey_ShouldFailWithoutSaving()
    {
        var (handler, repository) = Build();

        var result = await handler.HandleAsync(
            new CreateSegmentCommand("beta testers", "Beta testers", null, SegmentDefinition.Empty, Actor),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.KeyInvalidFormat, result.Error);
        Assert.Equal(0, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WhenTheKeyBelongsToALiveSegment_ShouldReportADuplicate()
    {
        var (handler, repository) = Build();
        repository.Seed(Segment.Create("beta-testers", "Existing", null, null, Now, Actor).Value);

        var result = await handler.HandleAsync(
            new CreateSegmentCommand("beta-testers", "Beta testers", null, SegmentDefinition.Empty, Actor),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("Segment.DuplicateKey", result.Error.Code);
        Assert.Equal(0, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WhenTheKeyBelongsToARetiredSegment_ShouldSaySoRatherThanCallItADuplicate()
    {
        // Two different problems with two different fixes: a duplicate means pick another name,
        // a retired key means this one is gone for good and its history is still under it.
        var (handler, repository) = Build();
        var retired = Segment.Create("beta-testers", "Existing", null, null, Now, Actor).Value;
        retired.Delete(Now, Actor);
        repository.Seed(retired);

        var result = await handler.HandleAsync(
            new CreateSegmentCommand("beta-testers", "Beta testers", null, SegmentDefinition.Empty, Actor),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("Segment.KeyRetired", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenTheStoreReportsAConflict_ShouldSurfaceIt()
    {
        // Another writer took the key between the check above and this insert; the unique index is
        // what actually settles it.
        var (handler, repository) = Build();
        var key = SegmentKey.Create("beta-testers").Value;
        repository.FailNextSaveWith(SegmentErrors.DuplicateKey(key));

        var result = await handler.HandleAsync(
            new CreateSegmentCommand("beta-testers", "Beta testers", null, SegmentDefinition.Empty, Actor),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.DuplicateKey(key), result.Error);
    }
}
