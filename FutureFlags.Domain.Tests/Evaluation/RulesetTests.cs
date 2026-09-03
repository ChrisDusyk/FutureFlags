using FutureFlags.Evaluation;

namespace FutureFlags.Domain.Tests.Evaluation;

/// <summary>
/// <see cref="Ruleset.SegmentsByKey"/> is memoized rather than rebuilt per call — it sits on every
/// single-flag lookup on the .NET client's hot path, and used to sit inside a per-flag loop on the
/// server's <c>GET /api/evaluation</c> route besides. These pin the memoization itself; correctness
/// of the index's contents is already covered by the conformance vectors.
/// </summary>
public class RulesetTests
{
    private static Ruleset OneSegmentRuleset() => new(
        "dev",
        [],
        [new RulesetSegment("beta-testers", [], [], [])]);

    [Fact]
    public void SegmentsByKey_CalledTwice_ShouldReturnTheSameInstance()
    {
        var ruleset = OneSegmentRuleset();

        var first = ruleset.SegmentsByKey();
        var second = ruleset.SegmentsByKey();

        Assert.Same(first, second);
    }

    [Fact]
    public void SegmentsByKey_OnTwoDifferentRulesetInstances_ShouldNotShareAnIndex()
    {
        // Memoization is per instance, not global — two rulesets built moments apart during a
        // refresh must never be able to see each other's cached index.
        var first = OneSegmentRuleset();
        var second = OneSegmentRuleset();

        Assert.NotSame(first.SegmentsByKey(), second.SegmentsByKey());
    }

    [Fact]
    public void SegmentsByKey_ShouldStillContainEverySegment()
    {
        var ruleset = OneSegmentRuleset();

        var index = ruleset.SegmentsByKey();

        Assert.True(index.ContainsKey("beta-testers"));
    }
    [Fact]
    public void TheDefaultVariantSet_ShouldNotBeMutableThroughACast()
    {
        // One instance is shared by every flag that arrives without variants — and a ruleset from a
        // server predating them is entirely such flags. Declaring the field as IReadOnlyDictionary
        // is not enough on its own: a caller can cast the property back to the Dictionary behind it
        // and write, corrupting variant lookup for every one of those flags in the process.
        var flag = new RulesetFlag("f", true, []);

        Assert.False(flag.Variants is Dictionary<string, FlagValue>);
        Assert.Equal(FlagValue.True, flag.OnValue);
        Assert.Equal(FlagValue.False, flag.OffValue);
    }

    [Fact]
    public void TwoFlagsWithoutVariants_ShouldReadTheSameDefaults()
    {
        // They share the instance, which is why the guard above matters.
        var one = new RulesetFlag("one", true, []);
        var two = new RulesetFlag("two", true, []);

        Assert.Equal(one.OnValue, two.OnValue);
        Assert.Equal(one.OffValue, two.OffValue);
    }

}
