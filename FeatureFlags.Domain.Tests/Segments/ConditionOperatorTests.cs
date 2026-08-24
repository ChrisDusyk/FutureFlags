using FeatureFlags.Domain.Segments;
using FeatureFlags.Evaluation;

namespace FeatureFlags.Domain.Tests.Segments;

public class ConditionOperatorTests
{
    [Fact]
    public void Create_ShouldRecognizeEveryOperatorItOffers()
    {
        Assert.All(ConditionOperator.All, @operator =>
        {
            var result = ConditionOperator.Create(@operator.Value);

            Assert.True(result.IsSuccess);
            Assert.Same(@operator, result.Value);
        });
    }

    [Theory]
    [InlineData("ONE-OF")]
    [InlineData("  one-of  ")]
    public void Create_ShouldNormalizeCasingAndWhitespace(string value)
    {
        var result = ConditionOperator.Create(value);

        Assert.True(result.IsSuccess);
        Assert.Same(ConditionOperator.OneOf, result.Value);
    }

    [Fact]
    public void Create_WithAnOperatorThisBuildDoesNotHave_ShouldFail()
    {
        var result = ConditionOperator.Create("matches");

        Assert.True(result.IsFailure);
        // The message lists what it does understand, which is the only useful thing to say to
        // somebody who just discovered there is no regex operator.
        Assert.Contains("one-of", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_WithoutAnOperator_ShouldFailAsRequired()
    {
        Assert.Equal(SegmentErrors.OperatorRequired, ConditionOperator.Create(" ").Error);
    }

    [Fact]
    public void FromPersisted_WithAnUnknownValue_ShouldThrowRatherThanInventAnOperator()
    {
        Assert.Throws<InvalidOperationException>(() => ConditionOperator.FromPersisted("matches"));
    }

    [Fact]
    public void TextOperators_ShouldAcceptOnlyText()
    {
        foreach (var @operator in new[] { ConditionOperator.Contains, ConditionOperator.StartsWith, ConditionOperator.EndsWith })
        {
            Assert.True(@operator.AcceptsKind(AttributeValueKind.Text));
            Assert.False(@operator.AcceptsKind(AttributeValueKind.Number));
            Assert.False(@operator.AcceptsKind(AttributeValueKind.Boolean));
        }
    }

    [Fact]
    public void NumericOperators_ShouldAcceptOnlyNumbers()
    {
        foreach (var @operator in new[]
        {
            ConditionOperator.GreaterThan,
            ConditionOperator.GreaterThanOrEqual,
            ConditionOperator.LessThan,
            ConditionOperator.LessThanOrEqual,
        })
        {
            Assert.True(@operator.AcceptsKind(AttributeValueKind.Number));
            Assert.False(@operator.AcceptsKind(AttributeValueKind.Text));
            Assert.False(@operator.AcceptsKind(AttributeValueKind.Boolean));
        }
    }

    [Fact]
    public void EqualityOperators_ShouldAcceptEveryKind()
    {
        foreach (var @operator in new[] { ConditionOperator.EqualTo, ConditionOperator.OneOf })
        {
            Assert.True(@operator.AcceptsKind(AttributeValueKind.Text));
            Assert.True(@operator.AcceptsKind(AttributeValueKind.Number));
            Assert.True(@operator.AcceptsKind(AttributeValueKind.Boolean));
        }
    }

    [Fact]
    public void OneOf_ShouldBeTheOnlyMultiValuedOperator()
    {
        Assert.Equal(
            [ConditionOperator.OneOf],
            ConditionOperator.All.Where(candidate => candidate.IsMultiValued));
    }

    [Fact]
    public void EveryOperatorValue_ShouldFitTheColumn()
    {
        Assert.All(ConditionOperator.All, @operator =>
            Assert.True(@operator.Value.Length <= ConditionOperator.MaxLength));
    }
}
