using FeatureFlags.Domain.Segments;
using FeatureFlags.Evaluation;

namespace FeatureFlags.Domain.Tests.Segments;

public class SegmentConditionTests
{
    [Fact]
    public void Create_ShouldFoldTheAttributeNameToLowercase()
    {
        var condition = SegmentCondition.Create("  AccountAgeDays  ", "greater-than", [AttributeValue.OfNumber(30)]).Value;

        Assert.Equal("accountagedays", condition.Attribute);
    }

    [Fact]
    public void Create_ShouldDeduplicateAndOrderValues()
    {
        var condition = SegmentCondition.Create(
            "plan",
            "one-of",
            [AttributeValue.OfText("team"), AttributeValue.OfText("pro"), AttributeValue.OfText("team")]).Value;

        Assert.Equal([AttributeValue.OfText("pro"), AttributeValue.OfText("team")], condition.Values);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutAnAttribute_ShouldFail(string? attribute)
    {
        var result = SegmentCondition.Create(attribute, "equals", [AttributeValue.OfText("x")]);

        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.AttributeRequired, result.Error);
    }

    [Fact]
    public void Create_WithNoValues_ShouldFail()
    {
        var result = SegmentCondition.Create("plan", "equals", []);

        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.ValuesRequired, result.Error);
    }

    [Fact]
    public void Create_WithSeveralValuesForASingleValuedOperator_ShouldFail()
    {
        var result = SegmentCondition.Create(
            "plan",
            "equals",
            [AttributeValue.OfText("pro"), AttributeValue.OfText("team")]);

        Assert.True(result.IsFailure);
        // Refused rather than silently comparing against only the first.
        Assert.Contains("one-of", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_WithRepeatedValuesForASingleValuedOperator_ShouldSucceed()
    {
        // They deduplicate to one before the arity is checked, so this is not a mistake to report.
        var result = SegmentCondition.Create(
            "plan",
            "equals",
            [AttributeValue.OfText("pro"), AttributeValue.OfText("pro")]);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Values);
    }

    [Fact]
    public void Create_WithAValueTheOperatorCannotCompare_ShouldFailWhenItIsWrittenRatherThanMatchNobodyLater()
    {
        var result = SegmentCondition.Create("age", "greater-than", [AttributeValue.OfText("30")]);

        Assert.True(result.IsFailure);
        Assert.Equal("Segment.Condition.ValueKindNotAccepted", result.Error.Code);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Create_WithANumberNoEngineCouldAgreeOn_ShouldFail(double value)
    {
        var result = SegmentCondition.Create("age", "greater-than", [AttributeValue.OfNumber(value)]);

        Assert.True(result.IsFailure);
        Assert.Equal("Segment.Condition.ValueNotRepresentable", result.Error.Code);
    }

    [Fact]
    public void Create_WithANumberPastTheSafeIntegerRange_ShouldFail()
    {
        // Past 2^53 a double and a JavaScript number stop agreeing on which integers exist.
        var result = SegmentCondition.Create("age", "greater-than", [AttributeValue.OfNumber(AttributeValue.MaxMagnitude * 2)]);

        Assert.True(result.IsFailure);
        Assert.Equal("Segment.Condition.ValueNotRepresentable", result.Error.Code);
    }

    [Fact]
    public void Create_WithAnOverlongTextValue_ShouldFail()
    {
        var result = SegmentCondition.Create(
            "note",
            "equals",
            [AttributeValue.OfText(new string('x', AttributeValue.MaxTextLength + 1))]);

        Assert.True(result.IsFailure);
        Assert.Equal("Segment.Condition.ValueNotRepresentable", result.Error.Code);
    }

    [Fact]
    public void Create_WithAnOverlongAttributeName_ShouldFail()
    {
        var result = SegmentCondition.Create(
            new string('a', SegmentCondition.MaxAttributeLength + 1),
            "equals",
            [AttributeValue.OfText("x")]);

        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.AttributeTooLong, result.Error);
    }

    [Fact]
    public void TwoConditionsWithTheSameContent_ShouldBeEqual()
    {
        var first = SegmentCondition.Create("plan", "one-of", [AttributeValue.OfText("pro"), AttributeValue.OfText("team")]).Value;
        var second = SegmentCondition.Create("Plan", "one-of", [AttributeValue.OfText("team"), AttributeValue.OfText("pro")]).Value;

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }
}
