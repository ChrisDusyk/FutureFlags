using FeatureFlags.Domain.Segments;
using FeatureFlags.Evaluation;

namespace FeatureFlags.Domain.Tests.Segments;

public class SegmentDefinitionTests
{
    private static SegmentCondition Condition(string attribute, ConditionOperator @operator, params AttributeValue[] values) =>
        SegmentCondition.Create(attribute, @operator.Value, values).Value;

    [Fact]
    public void Create_ShouldDeduplicateAndOrderKeys()
    {
        var definition = SegmentDefinition.Create(
            ["user-9", "user-1", "user-9"],
            ["user-5", "user-5"],
            []).Value;

        Assert.Equal(["user-1", "user-9"], definition.IncludedKeys);
        Assert.Equal(["user-5"], definition.ExcludedKeys);
    }

    [Fact]
    public void Create_ShouldNotFoldTheCaseOfAContextKey()
    {
        // Somebody else's identifier. Lowercasing it would silently target a different row.
        var definition = SegmentDefinition.Create(["User-17"], [], []).Value;

        Assert.Equal(["User-17"], definition.IncludedKeys);
    }

    [Fact]
    public void Create_ShouldDropBlankKeysRatherThanStoreThem()
    {
        var definition = SegmentDefinition.Create(["user-1", "", "   ", "  user-2  "], [], []).Value;

        Assert.Equal(["user-1", "user-2"], definition.IncludedKeys);
    }

    [Fact]
    public void Create_ShouldKeepConditionsInTheOrderTheyWereWritten()
    {
        var region = Condition("region", ConditionOperator.EqualTo, AttributeValue.OfText("eu-west"));
        var plan = Condition("plan", ConditionOperator.EqualTo, AttributeValue.OfText("pro"));

        var definition = SegmentDefinition.Create([], [], [region, plan]).Value;

        // Order does not change the answer — conditions are ANDed — but reordering them would make
        // the editor and the history diff lie about what somebody wrote.
        Assert.Equal([region, plan], definition.Conditions);
    }

    [Fact]
    public void Create_ShouldDeduplicateIdenticalConditions()
    {
        var plan = Condition("plan", ConditionOperator.EqualTo, AttributeValue.OfText("pro"));
        var samePlan = Condition("PLAN", ConditionOperator.EqualTo, AttributeValue.OfText("pro"));

        var definition = SegmentDefinition.Create([], [], [plan, samePlan]).Value;

        Assert.Single(definition.Conditions);
    }

    [Fact]
    public void Create_WithTooManyConditions_ShouldFail()
    {
        var conditions = Enumerable
            .Range(0, SegmentDefinition.MaxConditions + 1)
            .Select(index => Condition($"a{index}", ConditionOperator.EqualTo, AttributeValue.OfText("x")));

        var result = SegmentDefinition.Create([], [], conditions);

        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.TooManyConditions, result.Error);
    }

    [Fact]
    public void Create_WithTooManyKeys_ShouldFail()
    {
        var keys = Enumerable.Range(0, SegmentDefinition.MaxKeys + 1).Select(index => $"user-{index}");

        var result = SegmentDefinition.Create(keys, [], []);

        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.TooManyKeys, result.Error);
    }

    [Fact]
    public void Create_WithAnOverlongContextKey_ShouldFail()
    {
        var result = SegmentDefinition.Create([new string('u', SegmentDefinition.MaxKeyLength + 1)], [], []);

        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.ContextKeyTooLong, result.Error);
    }

    [Fact]
    public void TwoDefinitionsWithTheSameContent_ShouldBeEqualWhateverOrderTheyWereBuiltIn()
    {
        // The whole point of the normal form. Without this, the console re-posting an unchanged
        // form raises an event, moves the timestamp, and churns every SDK's ETag.
        var first = SegmentDefinition.Create(
            ["user-2", "user-1"],
            ["user-3"],
            [Condition("plan", ConditionOperator.OneOf, AttributeValue.OfText("team"), AttributeValue.OfText("pro"))]).Value;

        var second = SegmentDefinition.Create(
            ["user-1", "user-2", "user-1"],
            ["user-3"],
            [Condition("PLAN", ConditionOperator.OneOf, AttributeValue.OfText("pro"), AttributeValue.OfText("team"))]).Value;

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void TwoDefinitionsDifferingOnlyInAValueType_ShouldNotBeEqual()
    {
        var text = SegmentDefinition.Create([], [], [Condition("tier", ConditionOperator.EqualTo, AttributeValue.OfText("2"))]).Value;
        var number = SegmentDefinition.Create([], [], [Condition("tier", ConditionOperator.EqualTo, AttributeValue.OfNumber(2))]).Value;

        Assert.NotEqual(text, number);
    }

    [Fact]
    public void Empty_ShouldBeEmptyAndMatchTheResultOfCreatingNothing()
    {
        Assert.True(SegmentDefinition.Empty.IsEmpty);
        Assert.Equal(SegmentDefinition.Empty, SegmentDefinition.Create([], [], []).Value);
    }

    [Fact]
    public void ADefinitionWithOnlyExclusions_ShouldStillCountAsEmpty()
    {
        // Nothing here can admit anybody, so nothing here can turn a targeted flag on.
        var definition = SegmentDefinition.Create([], ["user-1"], []).Value;

        Assert.True(definition.IsEmpty);
    }
}
