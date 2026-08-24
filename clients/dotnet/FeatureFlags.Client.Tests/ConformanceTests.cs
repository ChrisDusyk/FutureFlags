using FeatureFlags.Evaluation;

namespace FeatureFlags.Client.Tests;

/// <summary>
/// The shared conformance vectors, run against this package's compilation of the evaluator.
///
/// <para>
/// The same file <c>FeatureFlags.Domain.Tests</c> runs, and that is the point rather than a
/// duplication: the evaluator here is compiled for <c>netstandard2.0</c>, <c>net8.0</c>, and
/// <c>net10.0</c> from the same source the server compiles for <c>net10.0</c>. If a language or
/// library difference across those targets ever changed an answer — a comparison, a rounding, a
/// culture creeping into a string — this is where it surfaces.
/// </para>
/// </summary>
public class ConformanceTests
{
    private static readonly IReadOnlyList<SegmentCase> Segments =
        ConformanceVectors.Load<SegmentCase>("segments.json");

    private static readonly IReadOnlyList<FlagCase> Flags =
        ConformanceVectors.Load<FlagCase>("flags.json");

    public static TheoryData<string> SegmentCaseNames()
    {
        var names = new TheoryData<string>();

        foreach (var vector in Segments)
            names.Add(vector.Name);

        return names;
    }

    public static TheoryData<string> FlagCaseNames()
    {
        var names = new TheoryData<string>();

        foreach (var vector in Flags)
            names.Add(vector.Name);

        return names;
    }

    [Theory]
    [MemberData(nameof(SegmentCaseNames))]
    public void Matches_ShouldAgreeWithTheSharedVectors(string name)
    {
        var vector = Segments.Single(candidate => candidate.Name == name);

        Assert.Equal(vector.Matches, SegmentMatcher.Matches(vector.Segment, vector.Context?.ToContext()));
    }

    [Theory]
    [MemberData(nameof(FlagCaseNames))]
    public void EvaluateAll_ShouldAgreeWithTheSharedVectors(string name)
    {
        var vector = Flags.Single(candidate => candidate.Name == name);

        var evaluated = FlagEvaluator.EvaluateAll(vector.Ruleset, vector.Context?.ToContext());

        Assert.Equal(vector.Expected.Count, evaluated.Count);

        foreach (var expected in vector.Expected)
        {
            Assert.True(evaluated.TryGetValue(expected.Key, out var actual), $"No answer for '{expected.Key}'.");
            Assert.Equal(expected.Value, actual);
        }
    }
}
