using FeatureFlags.Evaluation;

namespace FeatureFlags.Domain.Tests.Evaluation;

/// <summary>
/// <see cref="FlagEvaluator"/> against the shared conformance vectors — the whole ruleset, not one
/// segment. See <see cref="SegmentMatcherConformanceTests"/> for what these files are for.
/// </summary>
public class FlagEvaluatorConformanceTests
{
    private static readonly IReadOnlyList<FlagCase> Cases =
        ConformanceVectors.Load<FlagCase>("flags.json");

    public static TheoryData<string> CaseNames()
    {
        var names = new TheoryData<string>();

        foreach (var vector in Cases)
            names.Add(vector.Name);

        return names;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EvaluateAll_ShouldAgreeWithTheSharedVectors(string name)
    {
        var vector = Cases.Single(candidate => candidate.Name == name);

        var evaluated = FlagEvaluator.EvaluateAll(vector.Ruleset, vector.Context?.ToContext());

        // Asserted both ways round: an engine that answered for a flag the vector never mentioned
        // would otherwise pass, and that is exactly the kind of drift this file exists to catch.
        Assert.Equal(vector.Expected.Count, evaluated.Count);

        foreach (var expected in vector.Expected)
        {
            Assert.True(evaluated.TryGetValue(expected.Key, out var actual), $"No answer for '{expected.Key}'.");
            Assert.Equal(expected.Value, actual);
        }
    }

    [Fact]
    public void EveryCase_ShouldHaveADistinctName()
    {
        Assert.Equal(Cases.Count, Cases.Select(vector => vector.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EvaluateAll_ShouldLookFlagKeysUpWithoutRegardToCase()
    {
        // Both SDKs have always compared flag keys case-insensitively. Losing that here would break
        // IsEnabled("New-Checkout") for anyone relying on it, silently and only at run time.
        var ruleset = new Ruleset("prod", [new RulesetFlag("new-checkout", true, [])], []);

        var evaluated = FlagEvaluator.EvaluateAll(ruleset, FlagContext.Empty);

        Assert.True(evaluated["New-Checkout"]);
    }
}
