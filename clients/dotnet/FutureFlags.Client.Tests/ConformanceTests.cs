using FutureFlags.Evaluation;

namespace FutureFlags.Client.Tests;

/// <summary>
/// The shared conformance vectors, run against this package's compilation of the evaluator.
///
/// <para>
/// The same file <c>FutureFlags.Domain.Tests</c> runs, and that is the point rather than a
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
        var context = vector.Context?.ToContext();

        var resolved = FlagEvaluator.ResolveAll(vector.Ruleset, context);

        Assert.Equal(vector.Expected.Count, resolved.Count);

        foreach (var expected in vector.Expected)
        {
            Assert.True(resolved.TryGetValue(expected.Key, out var actual), $"No answer for '{expected.Key}'.");

            Assert.Equal(expected.Value.Value, actual.Value);
            Assert.Equal(expected.Value.Variant, actual.Variant);
            Assert.Equal(expected.Value.Reason, actual.Reason);

            // Asserted even where the vector omits it, which is the whole reason for version 2: a
            // normal resolution must carry no error code, and a reason-only assertion would let a
            // regression set one alongside DEFAULT without anything going red.
            Assert.Equal(expected.Value.ErrorCode, actual.ErrorCode);

            // EvaluateAll is now a reading of ResolveAll, so the boolean surface every released SDK
            // depends on has to keep agreeing with it.
            Assert.Equal(actual.AsBoolean(), FlagEvaluator.EvaluateAll(vector.Ruleset, context)[expected.Key]);
        }

        foreach (var key in vector.Missing ?? [])
        {
            // The vector has to be telling the truth about the key being absent, or the assertions
            // below would pass against a flag that simply resolved to something else.
            var flag = vector.Ruleset?.Flags
                .FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));

            Assert.Null(flag);

            var resolution = FlagEvaluator.Resolve(flag, vector.Ruleset?.SegmentsByKey(), context);

            Assert.Equal(EvaluationReason.Error, resolution.Reason);
            Assert.Equal(EvaluationErrorCode.FlagNotFound, resolution.ErrorCode);
            Assert.Null(resolution.Variant);

            // Still false to a boolean caller, which is what every released SDK has always answered
            // for a key it does not carry.
            Assert.False(resolution.AsBoolean());
        }
    }
}
