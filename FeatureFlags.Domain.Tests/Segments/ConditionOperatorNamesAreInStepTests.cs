using FeatureFlags.Domain.Segments;
using FeatureFlags.Evaluation;

namespace FeatureFlags.Domain.Tests.Segments;

/// <summary>
/// The seam between the two halves of an operator.
///
/// <para>
/// <see cref="ConditionOperator"/> is the domain's validating value object and decides what may be
/// <em>written</em>. <see cref="ConditionOperatorNames"/> is shared source, compiled into the client
/// package as well, and decides what can be <em>evaluated</em>. They are separate because the shared
/// half may not depend on <c>Result</c> — and separate things drift.
/// </para>
/// <para>
/// If this fails, one of two bad things is true: an operator the console can save that no client can
/// evaluate (it will silently match nobody), or one a client would evaluate that nothing can produce.
/// </para>
/// </summary>
public class ConditionOperatorNamesAreInStepTests
{
    [Fact]
    public void TheTwoHalvesOfAnOperator_ShouldNameTheSameSet()
    {
        Assert.Equal(ConditionOperatorNames.All, ConditionOperator.All.Select(@operator => @operator.Value));
    }

    [Fact]
    public void EveryOperatorTheDomainCanCreate_ShouldBeOneTheEvaluatorRecognises()
    {
        Assert.All(ConditionOperator.All, @operator =>
            Assert.True(ConditionOperatorNames.IsRecognised(@operator.Value), @operator.Value));
    }

    [Fact]
    public void EveryOperatorTheEvaluatorRecognises_ShouldBeOneTheDomainCanCreate()
    {
        Assert.All(ConditionOperatorNames.All, name =>
            Assert.True(ConditionOperator.Create(name).IsSuccess, name));
    }

    [Fact]
    public void NeitherHalf_ShouldOfferARegularExpressionOperator()
    {
        // Not an oversight, and this is here so that adding one is a deliberate act with a failing
        // test in front of it. See shared/evaluation/README.md: it cannot be made safe in a browser,
        // and validating patterns server-side does not rescue it — (a+)+b compiles happily under
        // RegexOptions.NonBacktracking.
        Assert.False(ConditionOperatorNames.IsRecognised("matches"));
        Assert.True(ConditionOperator.Create("matches").IsFailure);
    }
}
