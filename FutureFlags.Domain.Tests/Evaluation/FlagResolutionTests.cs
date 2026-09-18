using FutureFlags.Evaluation;

namespace FutureFlags.Domain.Tests.Evaluation;

public class FlagResolutionTests
{
    [Fact]
    public void TheDefaultMetadata_ShouldNotBeMutableThroughACast()
    {
        // Every resolution built without metadata of its own shares this instance — a cast-and-write
        // would leak metadata across every unrelated resolution in the process for the rest of its
        // life. Declaring the field as IReadOnlyDictionary is not enough on its own: a caller can
        // cast the property back to the Dictionary behind it and write.
        var resolution = new FlagResolution(FlagValue.True, "on", EvaluationReason.Static);

        Assert.False(resolution.FlagMetadata is Dictionary<string, AttributeValue>);
        Assert.Empty(resolution.FlagMetadata);
    }

    [Fact]
    public void TwoResolutionsWithoutMetadata_ShouldShareTheSameEmptyInstance()
    {
        // They share the instance, which is why the guard above matters.
        var one = new FlagResolution(FlagValue.True, "on", EvaluationReason.Static);
        var two = new FlagResolution(FlagValue.False, "off", EvaluationReason.Disabled);

        Assert.Same(one.FlagMetadata, two.FlagMetadata);
    }
}
