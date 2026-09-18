using System.Text.Json;
using FutureFlags.Evaluation;

namespace FutureFlags.Domain.Tests.Evaluation;

/// <summary>
/// <see cref="FlagValue"/>, and in particular what <see cref="FlagValue.IsRepresentable"/> is
/// willing to vouch for.
/// </summary>
public class FlagValueTests
{
    [Fact]
    public void ABooleanValue_ShouldSerializeAsABarePrimitive()
    {
        // Bare, not wrapped: it is what lets an OpenFeature consumer read a value without
        // unwrapping anything of ours.
        Assert.Equal("true", JsonSerializer.Serialize(FlagValue.True, RulesetJson.Options));
        Assert.Equal("false", JsonSerializer.Serialize(FlagValue.False, RulesetJson.Options));
    }

    [Theory]
    [InlineData("{\"a\":1}")]
    [InlineData("[1,2,3]")]
    [InlineData("{}")]
    public void AnObjectValueThatParses_ShouldBeRepresentable(string json) =>
        Assert.True(FlagValue.OfObject(json).IsRepresentable);

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"a\":")]
    [InlineData("")]
    public void AnObjectValueThatDoesNotParse_ShouldNotBeRepresentable(string json)
    {
        // The converter writes an object value with WriteRawValue, which validates and throws. Left
        // unchecked, an unparseable value would pass FlagVariants.Create and then fail while
        // serializing a ruleset or writing an event — a long way from whoever supplied it.
        Assert.False(FlagValue.OfObject(json).IsRepresentable);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("\"text\"")]
    [InlineData("true")]
    [InlineData("null")]
    public void AnObjectValueThatIsNotAStructure_ShouldNotBeRepresentable(string json)
    {
        // Valid JSON, but the kind would say object while the token on the wire said number, string
        // or boolean. A reader cannot be expected to reconcile that.
        Assert.False(FlagValue.OfObject(json).IsRepresentable);
    }

    [Fact]
    public void ARepresentableObjectValue_ShouldActuallySerialize()
    {
        // The property and the converter have to agree, which is the whole point of checking.
        var value = FlagValue.OfObject("""{"a":[1,2]}""");

        Assert.True(value.IsRepresentable);
        Assert.Equal("""{"a":[1,2]}""", JsonSerializer.Serialize(value, RulesetJson.Options));
    }

    [Fact]
    public void AnOverLongObjectValue_ShouldNotBeRepresentable()
    {
        var oversized = "{\"a\":\"" + new string('x', FlagValue.MaxObjectJsonLength) + "\"}";

        Assert.False(FlagValue.OfObject(oversized).IsRepresentable);
    }

    [Fact]
    public void AnOverLongStringValue_ShouldNotBeRepresentable() =>
        Assert.False(FlagValue.OfString(new string('x', FlagValue.MaxTextLength + 1)).IsRepresentable);

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ANumberNoRuntimeCanCarry_ShouldNotBeRepresentable(double number) =>
        Assert.False(FlagValue.OfNumber(number).IsRepresentable);

    [Fact]
    public void ANumberPastTwoToTheFiftyThree_ShouldNotBeRepresentable() =>
        Assert.False(FlagValue.OfNumber(FlagValue.MaxMagnitude + 2).IsRepresentable);

    [Fact]
    public void TheCanonicalRendering_ShouldLeadWithTheKind()
    {
        // So that "1", 1, and true can never collide in a fingerprint.
        Assert.NotEqual(FlagValue.OfString("1").ToCanonicalString(), FlagValue.OfNumber(1).ToCanonicalString());
        Assert.NotEqual(FlagValue.True.ToCanonicalString(), FlagValue.OfString("true").ToCanonicalString());
    }

    [Fact]
    public void AnObjectValue_ShouldRoundTripThroughTheConverter()
    {
        const string json = """{"nested":{"a":1},"list":[1,2]}""";

        var read = JsonSerializer.Deserialize<FlagValue>(json, RulesetJson.Options)!;

        Assert.Equal(FlagValueKind.Object, read.Kind);
        Assert.True(read.IsRepresentable);
        Assert.Equal(json, JsonSerializer.Serialize(read, RulesetJson.Options));
    }
}
