using FutureFlags.Evaluation;

namespace FutureFlags.Domain.Tests.Evaluation;

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
        var context = vector.Context?.ToContext();

        var resolved = FlagEvaluator.ResolveAll(vector.Ruleset, context);

        // Asserted both ways round: an engine that answered for a flag the vector never mentioned
        // would otherwise pass, and that is exactly the kind of drift this file exists to catch.
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
