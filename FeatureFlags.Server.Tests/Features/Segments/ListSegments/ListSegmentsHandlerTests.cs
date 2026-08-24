using FeatureFlags.Domain.Segments;
using FeatureFlags.Evaluation;
using FeatureFlags.Server.Features.Segments.ListSegments;
using FeatureFlags.Server.Tests.Fakes;

namespace FeatureFlags.Server.Tests.Features.Segments.ListSegments;

public class ListSegmentsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    private static SegmentView View(string key, SegmentDefinition definition) => new(
        Guid.CreateVersion7(), SegmentKey.Create(key).Value, key, string.Empty, definition, Now, Now);

    [Fact]
    public async Task HandleAsync_ShouldReturnEverySegmentOrderedByKey()
    {
        var repository = new FakeSegmentViewRepository();
        repository.Seed(View("internal-staff", SegmentDefinition.Empty));
        repository.Seed(View("beta-testers", SegmentDefinition.Empty));

        var result = await new ListSegmentsHandler(repository).HandleAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(["beta-testers", "internal-staff"], result.Value.Segments.Select(segment => segment.Key));
    }

    [Fact]
    public async Task HandleAsync_ShouldSummarizeADefinitionRatherThanShipIt()
    {
        var definition = SegmentDefinition.Create(
            ["user-1", "user-2"],
            ["user-3"],
            [
                SegmentCondition.Create("plan", "equals", [AttributeValue.OfText("pro")]).Value,
                SegmentCondition.Create("seats", "greater-than", [AttributeValue.OfNumber(10)]).Value,
            ]).Value;

        var repository = new FakeSegmentViewRepository();
        repository.Seed(View("beta-testers", definition));

        var result = await new ListSegmentsHandler(repository).HandleAsync(TestContext.Current.CancellationToken);

        var summary = result.Value.Segments.Single();
        Assert.Equal(2, summary.ConditionCount);
        Assert.Equal(2, summary.IncludedKeyCount);
        Assert.Equal(1, summary.ExcludedKeyCount);
        Assert.False(summary.IsEmptyDefinition);
    }

    [Fact]
    public async Task HandleAsync_ShouldFlagAnEmptyDefinition()
    {
        // Worth saying in the list rather than leaving somebody to infer it from three zeroes: an
        // empty definition silently turns off every flag that targets it. IsEmptyDefinition is
        // named for what it checks — structural emptiness — not "matches nobody" in general, which
        // a definition can also do through mutually exclusive conditions this does not detect.
        var repository = new FakeSegmentViewRepository();
        repository.Seed(View("half-finished", SegmentDefinition.Create([], ["user-1"], []).Value));

        var result = await new ListSegmentsHandler(repository).HandleAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Value.Segments.Single().IsEmptyDefinition);
    }

    [Fact]
    public async Task HandleAsync_WithNoSegments_ShouldReturnAnEmptyList()
    {
        var result = await new ListSegmentsHandler(new FakeSegmentViewRepository())
            .HandleAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Segments);
    }
}
