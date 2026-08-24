using FeatureFlags.Evaluation;

namespace FeatureFlags.Domain.Tests.Evaluation;

/// <summary>
/// <see cref="SegmentMatcher"/> against the shared conformance vectors.
///
/// <para>
/// These are the same cases the .NET client and the Node client run. This suite and the client's
/// run the very same code — the matcher is shared source, linked into both — so what they really
/// prove is that the netstandard2.0, net8.0, and net10.0 compilations of it agree. The Node suite
/// is the one that polices a genuinely independent implementation, and it is why the file is JSON
/// rather than a C# theory.
/// </para>
/// </summary>
public class SegmentMatcherConformanceTests
{
    public static TheoryData<string> CaseNames()
    {
        var names = new TheoryData<string>();

        foreach (var vector in Cases)
            names.Add(vector.Name);

        return names;
    }

    private static readonly IReadOnlyList<SegmentCase> Cases =
        ConformanceVectors.Load<SegmentCase>("segments.json");

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Matches_ShouldAgreeWithTheSharedVectors(string name)
    {
        var vector = Cases.Single(candidate => candidate.Name == name);

        var matched = SegmentMatcher.Matches(vector.Segment, vector.Context?.ToContext());

        Assert.Equal(vector.Matches, matched);
    }

    [Fact]
    public void EveryCase_ShouldHaveADistinctName()
    {
        // The theory addresses a case by name, so a duplicate would silently run one case twice
        // and never run the other.
        Assert.Equal(Cases.Count, Cases.Select(vector => vector.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryCase_ShouldCarryASegment()
    {
        Assert.All(Cases, vector => Assert.NotNull(vector.Segment));
    }
}
