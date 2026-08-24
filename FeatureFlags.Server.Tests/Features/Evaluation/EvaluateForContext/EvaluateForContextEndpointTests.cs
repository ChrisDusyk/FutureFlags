using FeatureFlags.Evaluation;
using FeatureFlags.Server.Features.Evaluation.EvaluateForContext;

namespace FeatureFlags.Server.Tests.Features.Evaluation.EvaluateForContext;

public class EvaluateForContextEndpointTests
{
    [Fact]
    public void Bind_WithNoContext_ShouldSucceedWithAnEmptyOne()
    {
        var result = EvaluateForContextEndpoint.Bind(null);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Attributes);
        Assert.Null(result.Value.Key);
    }

    // The shared evaluator's own normalisation, a segment condition's validation, and the Node
    // client's normalizeContext all treat a null attribute value as an absent one, not a fourth
    // kind. This route has to agree, or the same context reads differently depending on whether a
    // ffs_ key evaluates it client-side or a ffp_ key posts it here.
    [Fact]
    public void Bind_WithANullAttributeValue_ShouldDropItRatherThanRefuseTheContext()
    {
        // AttributeValue is a reference type, so a JSON `null` deserializes as an actual null
        // entry here rather than being caught by the model binder — see AttributeValueJsonConverter.
        var request = new EvaluateForContextContextRequest(
            "user-1",
            new Dictionary<string, AttributeValue> { ["plan"] = null! });

        var result = EvaluateForContextEndpoint.Bind(request);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.TryGetAttribute("plan", out _));
    }

    [Fact]
    public void Bind_WithAnUnrepresentableAttributeValue_ShouldRefuseTheContext()
    {
        var request = new EvaluateForContextContextRequest(
            "user-1",
            new Dictionary<string, AttributeValue> { ["plan"] = AttributeValue.OfText(new string('x', 1000)) });

        var result = EvaluateForContextEndpoint.Bind(request);

        Assert.True(result.IsFailure);
        Assert.Equal("Evaluation.Context.AttributeNotRepresentable", result.Error.Code);
    }

    [Fact]
    public void Bind_WithNullValuesAlongsideRealOnes_ShouldNotCountThemTowardTheCap()
    {
        var attributes = new Dictionary<string, AttributeValue>(StringComparer.Ordinal);
        for (var i = 0; i < 64; i++)
            attributes[$"attr{i}"] = AttributeValue.OfText("value");

        // 64 real attributes plus a null one: the null must not push the count over the cap.
        attributes["unset"] = null!;

        var request = new EvaluateForContextContextRequest("user-1", attributes);

        var result = EvaluateForContextEndpoint.Bind(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(64, result.Value.Attributes.Count);
    }

    [Fact]
    public void Bind_With65RealAttributes_ShouldRefuseTheContext()
    {
        var attributes = new Dictionary<string, AttributeValue>(StringComparer.Ordinal);
        for (var i = 0; i < 65; i++)
            attributes[$"attr{i}"] = AttributeValue.OfText("value");

        var request = new EvaluateForContextContextRequest("user-1", attributes);

        var result = EvaluateForContextEndpoint.Bind(request);

        Assert.True(result.IsFailure);
        Assert.Equal("Evaluation.Context.TooLarge", result.Error.Code);
    }
}
