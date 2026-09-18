using System.Text.Json;
using FutureFlags.Evaluation;
using FutureFlags.Server.Evaluation;

namespace FutureFlags.Server.Tests.Features.Evaluation.Ofrep;

/// <summary>
/// Reading an OpenFeature evaluation context, which is flat where this platform's own is nested,
/// and which permits value kinds <see cref="AttributeValue"/> cannot hold.
/// </summary>
public class OfrepContextTests
{
    private static IReadOnlyDictionary<string, JsonElement> Context(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    [Fact]
    public void BindContext_WithNothing_ShouldBeTheEmptyContext()
    {
        var result = Server.Evaluation.Ofrep.BindContext(null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Key);
        Assert.Empty(result.Value.Attributes);
    }

    [Fact]
    public void BindContext_ShouldReadTargetingKeyAsTheContextKey()
    {
        var result = Server.Evaluation.Ofrep.BindContext(Context("""{"targetingKey":"user-17"}"""));

        Assert.Equal("user-17", result.Value.Key);

        // And it is not left behind as an attribute named targetingKey.
        Assert.Empty(result.Value.Attributes);
    }

    [Fact]
    public void BindContext_ShouldAcceptKeyAsAnAlias()
    {
        // So a caller already speaking FutureFlags can point at these routes without rewriting its
        // context. Without this, such a context would evaluate as anonymous and every segment would
        // quietly stop matching.
        var result = Server.Evaluation.Ofrep.BindContext(Context("""{"key":"user-17"}"""));

        Assert.Equal("user-17", result.Value.Key);
        Assert.Empty(result.Value.Attributes);
    }

    [Fact]
    public void BindContext_WithBoth_ShouldPreferTheSpecsField()
    {
        var result = Server.Evaluation.Ofrep.BindContext(
            Context("""{"targetingKey":"spec","key":"alias"}"""));

        Assert.Equal("spec", result.Value.Key);
    }

    [Fact]
    public void BindContext_ShouldReadCustomFieldsAsAttributes()
    {
        var result = Server.Evaluation.Ofrep.BindContext(
            Context("""{"targetingKey":"user-17","plan":"enterprise","age":30,"beta":true}"""));

        Assert.True(result.Value.TryGetAttribute("plan", out var plan));
        Assert.Equal(AttributeValue.OfText("enterprise"), plan);

        Assert.True(result.Value.TryGetAttribute("age", out var age));
        Assert.Equal(AttributeValue.OfNumber(30), age);

        Assert.True(result.Value.TryGetAttribute("beta", out var beta));
        Assert.Equal(AttributeValue.OfBoolean(true), beta);
    }

    [Theory]
    [InlineData("""{"nested":{"a":1}}""")]
    [InlineData("""{"list":[1,2,3]}""")]
    [InlineData("""{"unset":null}""")]
    public void BindContext_WithAValueThisPlatformCannotHold_ShouldDropItRatherThanFail(string json)
    {
        // OpenFeature's context permits structures, lists and nulls. Failing the request over one
        // would mean a client that adds an unrelated object to its context stops getting any flags
        // at all — far worse than one attribute no rule could have used.
        var result = Server.Evaluation.Ofrep.BindContext(Context(json));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Attributes);
    }

    [Fact]
    public void BindContext_WithADatetime_ShouldKeepItAsText()
    {
        // A datetime arrives as a JSON string, and text is the only form three runtimes compare the
        // same way. Nothing parses it.
        var result = Server.Evaluation.Ofrep.BindContext(
            Context("""{"signedUpAt":"2026-02-20T21:28:18Z"}"""));

        Assert.True(result.Value.TryGetAttribute("signedUpAt", out var value));
        Assert.Equal(AttributeValue.OfText("2026-02-20T21:28:18Z"), value);
    }

    [Fact]
    public void BindContext_WithAnUnrepresentableValue_ShouldDropIt()
    {
        var result = Server.Evaluation.Ofrep.BindContext(
            Context($$"""{"plan":"{{new string('x', 1000)}}"}"""));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Attributes);
    }

    [Fact]
    public void BindContext_WithTooManyAttributes_ShouldRefuseTheContext()
    {
        var fields = string.Join(",", Enumerable.Range(0, 65).Select(i => $"\"attr{i}\":\"v\""));

        var result = Server.Evaluation.Ofrep.BindContext(Context($"{{{fields}}}"));

        Assert.True(result.IsFailure);
        Assert.Equal(EvaluationErrorCode.InvalidContext, result.Error.Code);
    }

    [Fact]
    public void BindContext_WithAnOverLongTargetingKey_ShouldRefuseTheContext()
    {
        var result = Server.Evaluation.Ofrep.BindContext(
            Context($$"""{"targetingKey":"{{new string('k', 257)}}"}"""));

        Assert.True(result.IsFailure);
        Assert.Equal(EvaluationErrorCode.InvalidContext, result.Error.Code);
    }

    [Fact]
    public void BindContext_WithUnusableFieldsAlongsideRealOnes_ShouldNotCountThemTowardTheCap()
    {
        var fields = string.Join(",", Enumerable.Range(0, 64).Select(i => $"\"attr{i}\":\"v\""));

        var result = Server.Evaluation.Ofrep.BindContext(
            Context($$"""{{{fields}},"nested":{"a":1},"unset":null}"""));

        Assert.True(result.IsSuccess);
        Assert.Equal(64, result.Value.Attributes.Count);
    }

    [Fact]
    public void ETagFor_ShouldChangeWithTheContext()
    {
        // The property the whole conditional-POST arrangement rests on. If a tag depended only on
        // the ruleset, a client that changed its context and reused the tag it was last given
        // would be told nothing had changed when everything had.
        var one = Server.Evaluation.Ofrep.ETagFor("\"ruleset\"", FlagContext.For("user-1"));
        var two = Server.Evaluation.Ofrep.ETagFor("\"ruleset\"", FlagContext.For("user-2"));

        Assert.NotEqual(one, two);
    }

    [Fact]
    public void ETagFor_ShouldChangeWithTheRuleset()
    {
        var one = Server.Evaluation.Ofrep.ETagFor("\"before\"", FlagContext.For("user-1"));
        var two = Server.Evaluation.Ofrep.ETagFor("\"after\"", FlagContext.For("user-1"));

        Assert.NotEqual(one, two);
    }

    [Fact]
    public void ETagFor_ShouldNotDependOnAttributeOrder()
    {
        var one = FlagContext.For("user-1").With("a", "1").With("b", "2");
        var two = FlagContext.For("user-1").With("b", "2").With("a", "1");

        Assert.Equal(
            Server.Evaluation.Ofrep.ETagFor("\"ruleset\"", one),
            Server.Evaluation.Ofrep.ETagFor("\"ruleset\"", two));
    }

    [Fact]
    public void ETagFor_ShouldBeQuoted()
    {
        // A bare ETag is silently ignored by anything that reads the header properly.
        var etag = Server.Evaluation.Ofrep.ETagFor("\"ruleset\"", FlagContext.Empty);

        Assert.StartsWith("\"", etag, StringComparison.Ordinal);
        Assert.EndsWith("\"", etag, StringComparison.Ordinal);
    }
}
