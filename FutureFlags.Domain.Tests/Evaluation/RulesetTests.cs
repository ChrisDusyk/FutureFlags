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
}
