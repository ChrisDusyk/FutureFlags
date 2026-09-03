using System.Text.Json;
using FutureFlags.Evaluation;

namespace FutureFlags.Client.Tests;

/// <summary>
/// That the ruleset wire shape stays readable in both directions across the variants change.
///
/// <para>
/// The three evaluation routes are a documented compatibility surface, and SDK versions in the wild
/// are not upgraded in step with the server. So a ruleset written before flags had variants has to
/// parse here, and one written after has to parse in a client that predates them. Neither direction
/// has anything else asserting it.
/// </para>
/// </summary>
public class RulesetCompatibilityTests
{
    [Fact]
    public void ARulesetWithoutVariants_ShouldReadAsBoolean()
    {
        // Exactly what a server predating this change sends.
        const string json = """
            {
              "environment": "prod",
              "flags": [{ "key": "new-checkout", "isEnabled": true, "targetedSegments": [] }],
              "segments": []
            }
            """;

        var ruleset = JsonSerializer.Deserialize<Ruleset>(json, RulesetJson.Options);

        Assert.NotNull(ruleset);

        var flag = Assert.Single(ruleset.Flags);

        Assert.Equal(FlagValueTypeNames.Boolean, flag.ValueType);
        Assert.Equal(FlagVariantNames.On, flag.OnVariant);
        Assert.Equal(FlagVariantNames.Off, flag.OffVariant);
        Assert.Equal(FlagValue.True, flag.OnValue);
        Assert.Equal(FlagValue.False, flag.OffValue);

        // And the answer is the one it has always been.
        Assert.True(FlagEvaluator.Evaluate(flag, ruleset.SegmentsByKey(), FlagContext.Empty));
    }

    [Fact]
    public void ARulesetWithVariants_ShouldStillCarryTheFieldsAnOlderClientReads()
    {
        // The other direction: key, isEnabled and targetedSegments keep their names and meanings,
        // which is what lets a released SDK read a new server's payload. Asserted on the serialized
        // form rather than the object, because it is the JSON an older client parses.
        var ruleset = new Ruleset(
            "prod",
            [
                new RulesetFlag(
                    "new-checkout",
                    true,
                    ["beta"],
                    FlagValueTypeNames.Boolean,
                    new Dictionary<string, FlagValue> { ["on"] = FlagValue.True, ["off"] = FlagValue.False },
                    FlagVariantNames.On,
                    FlagVariantNames.Off),
            ],
            []);

        var json = JsonSerializer.Serialize(ruleset, RulesetJson.Options);

        Assert.Contains("\"key\":\"new-checkout\"", json, StringComparison.Ordinal);
        Assert.Contains("\"isEnabled\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"targetedSegments\":[\"beta\"]", json, StringComparison.Ordinal);

        // The variant values are bare JSON primitives, not a FutureFlags-shaped wrapper — which is
        // what lets an OpenFeature consumer read them without unwrapping anything.
        Assert.Contains("\"on\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"off\":false", json, StringComparison.Ordinal);
    }

    [Fact]
    public void AFlagNamingItsOwnVariants_ShouldServeThem()
    {
        const string json = """
            {
              "environment": "prod",
              "flags": [{
                "key": "renamed",
                "isEnabled": true,
                "targetedSegments": [],
                "valueType": "boolean",
                "variants": { "enabled": true, "disabled": false },
                "onVariant": "enabled",
                "offVariant": "disabled"
              }],
              "segments": []
            }
            """;

        var ruleset = JsonSerializer.Deserialize<Ruleset>(json, RulesetJson.Options)!;
        var resolution = FlagEvaluator.Resolve(ruleset.Flags[0], ruleset.SegmentsByKey(), FlagContext.Empty);

        Assert.Equal("enabled", resolution.Variant);
        Assert.Equal(FlagValue.True, resolution.Value);
        Assert.Equal(EvaluationReason.Static, resolution.Reason);
    }

    [Fact]
    public void AVariantNameWithNothingBehindIt_ShouldFallBackRatherThanFail()
    {
        // A hand-edited or foreign ruleset. A flag that stopped serving over a misconfiguration
        // would be a worse outcome than one that serves the boolean reading of its own variant name.
        const string json = """
            {
              "environment": "prod",
              "flags": [{
                "key": "broken",
                "isEnabled": true,
                "targetedSegments": [],
                "variants": {},
                "onVariant": "enabled",
                "offVariant": "disabled"
              }],
              "segments": []
            }
            """;

        var ruleset = JsonSerializer.Deserialize<Ruleset>(json, RulesetJson.Options)!;

        Assert.True(FlagEvaluator.Evaluate(ruleset.Flags[0], ruleset.SegmentsByKey(), FlagContext.Empty));
    }
}
