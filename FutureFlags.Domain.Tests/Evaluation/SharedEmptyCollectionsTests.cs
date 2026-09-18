using FutureFlags.Evaluation;

namespace FutureFlags.Domain.Tests.Evaluation;

/// <summary>
/// The shared empty collections this platform hands out, and the fact that none of them can be
/// written to.
///
/// <para>
/// Four places cache a single empty collection and give the same instance to every caller:
/// <c>FlagContext.NoAttributes</c>, <c>FlagResolution.NoMetadata</c>,
/// <c>RulesetFlag.DefaultVariants</c>, and the OFREP bulk handler's own metadata. Declaring the
/// field as <c>IReadOnlyDictionary</c> does not make it read-only — a caller can cast the public
/// property back to the <c>Dictionary</c> behind it and write, and every holder of that shared
/// instance sees the change for the life of the process.
/// </para>
/// <para>
/// This is one test rather than four assertions scattered across four files because the bug is the
/// pattern, not any one site. Three of the four were fixed one at a time as a reviewer found them;
/// the fourth was found by looking for the shape instead. A new shared empty collection belongs
/// here on the way in.
/// </para>
/// </summary>
public class SharedEmptyCollectionsTests
{
    [Fact]
    public void TheEmptyContextsAttributes_ShouldNotBeMutableThroughACast()
    {
        // The widest of the four: FlagContext.Empty is what every context-less evaluation runs
        // against, on the server and in both clients. A write here would inject traits into all of
        // them and silently change which segments match.
        Assert.False(FlagContext.Empty.Attributes is Dictionary<string, AttributeValue>);
    }

    [Fact]
    public void EveryContextWithoutAttributes_ShouldShareThatInstance()
    {
        // Which is what makes the guard above matter rather than being theoretical.
        Assert.Same(FlagContext.Empty.Attributes, FlagContext.For("user-17").Attributes);
        Assert.Same(FlagContext.Empty.Attributes, new FlagContext(null, null).Attributes);
    }

    [Fact]
    public void ADefaultResolutionsMetadata_ShouldNotBeMutableThroughACast()
    {
        var resolution = new FlagResolution(FlagValue.True, FlagVariantNames.On, EvaluationReason.Static);

        Assert.False(resolution.FlagMetadata is Dictionary<string, AttributeValue>);
    }

    [Fact]
    public void EveryResolutionWithoutMetadata_ShouldShareThatInstance()
    {
        var one = new FlagResolution(FlagValue.True, FlagVariantNames.On, EvaluationReason.Static);
        var two = new FlagResolution(FlagValue.False, FlagVariantNames.Off, EvaluationReason.Disabled);

        Assert.Same(one.FlagMetadata, two.FlagMetadata);
    }

    [Fact]
    public void ADefaultVariantSet_ShouldNotBeMutableThroughACast()
    {
        var flag = new RulesetFlag("f", true, []);

        Assert.False(flag.Variants is Dictionary<string, FlagValue>);
    }

    [Fact]
    public void AResolvedFlagsMetadata_ShouldNotBeMutableThroughACast()
    {
        // Reached the way a caller actually gets one, rather than by constructing a resolution
        // directly — the evaluator is what hands these out in practice.
        var ruleset = new Ruleset("prod", [new RulesetFlag("f", true, [])], []);
        var resolved = FlagEvaluator.Resolve(ruleset.Flags[0], ruleset.SegmentsByKey(), FlagContext.Empty);

        Assert.False(resolved.FlagMetadata is Dictionary<string, AttributeValue>);
        Assert.Empty(resolved.FlagMetadata);
    }
}
