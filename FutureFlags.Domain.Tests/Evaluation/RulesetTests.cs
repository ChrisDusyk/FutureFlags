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

    [Fact]
    public void ASuppliedVariantSet_ShouldNotBeMutableThroughACast()
    {
        // RulesetProvider.Build hands this constructor a dictionary and then caches the resulting
        // RulesetFlag, so a cast-and-write here would corrupt every environment's shared, cached
        // ruleset for the life of the process — not just one flag's lookup, the way the default
        // variant set fix covers.
        var flag = new RulesetFlag(
            "f", true, [], FlagValueTypeNames.Boolean,
            new Dictionary<string, FlagValue>(StringComparer.Ordinal) { ["on"] = FlagValue.True, ["off"] = FlagValue.False },
            "on", "off");

        Assert.False(flag.Variants is Dictionary<string, FlagValue>);
    }

    [Fact]
    public void MutatingTheDictionaryPassedIn_ShouldNotChangeTheFlag()
    {
        // The constructor copies rather than adopting the caller's dictionary by reference, so a
        // caller who keeps mutating their own dictionary after construction cannot reach back in.
        var supplied = new Dictionary<string, FlagValue>(StringComparer.Ordinal) { ["on"] = FlagValue.True, ["off"] = FlagValue.False };
        var flag = new RulesetFlag("f", true, [], FlagValueTypeNames.Boolean, supplied, "on", "off");

        supplied["on"] = FlagValue.OfString("tampered");

        Assert.Equal(FlagValue.True, flag.Variants["on"]);
    }

    [Fact]
    public void ANullVariantValue_ShouldReadAsMissingRatherThanPropagatingNull()
    {
        // System.Text.Json never calls FlagValueJsonConverter.Read for a JSON null against this
        // reference-typed value, so a malformed ruleset payload such as {"on": null} hands the
        // constructor a dictionary whose "on" entry is a literal null FlagValue. OnValue/OffValue
        // must fall back the same way they do for a variant name with nothing behind it, rather than
        // handing callers a null they will dereference (Value.Kind) unconditionally.
        var supplied = new Dictionary<string, FlagValue>(StringComparer.Ordinal) { ["off"] = FlagValue.False };
        supplied["on"] = null!;
        var flag = new RulesetFlag("f", true, [], FlagValueTypeNames.Boolean, supplied, "on", "off");

        Assert.False(flag.Variants.ContainsKey("on"));
        Assert.Equal(FlagValue.True, flag.OnValue);
        Assert.Equal(FlagValue.False, flag.OffValue);
    }
}
