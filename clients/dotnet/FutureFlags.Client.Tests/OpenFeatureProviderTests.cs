using System.Net.Http;
using FutureFlags.Client.OpenFeature;
using FutureFlags.Evaluation;
using OpenFeature.Constant;
using OpenFeature.Model;

namespace FutureFlags.Client.Tests;

/// <summary>
/// The OpenFeature provider: a thin wrapper over the client's own resolution, so an application
/// reading through the OpenFeature SDK and one reading through this package's API cannot be told
/// different things.
/// </summary>
public class OpenFeatureProviderTests
{
    /// <summary>A client that answers with whatever resolution a test hands it.</summary>
    private sealed class StubClient(FlagResolution resolution) : IFutureFlagsClient
    {
        public FlagContext? LastContext { get; private set; }

        public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(resolution.AsBoolean());

        public Task<bool> IsEnabledAsync(string key, bool defaultValue, CancellationToken cancellationToken = default) =>
            Task.FromResult(resolution.AsBoolean(defaultValue));

        public Task<bool> IsEnabledAsync(string key, FlagContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(resolution.AsBoolean());

        public Task<bool> IsEnabledAsync(
            string key, FlagContext context, bool defaultValue, CancellationToken cancellationToken = default) =>
            Task.FromResult(resolution.AsBoolean(defaultValue));

        public Task<FlagResolution> ResolveAsync(
            string key, FlagContext context, CancellationToken cancellationToken = default)
        {
            LastContext = context;

            return Task.FromResult(resolution);
        }

        public Exception? RefreshFailure { get; set; }

        public Task RefreshAsync(CancellationToken cancellationToken = default) =>
            RefreshFailure is null ? Task.CompletedTask : Task.FromException(RefreshFailure);
    }

    private static FutureFlagsProvider ProviderFor(FlagResolution resolution) => new(new StubClient(resolution));

    [Fact]
    public void Metadata_ShouldNameTheProvider() =>
        Assert.Equal("FutureFlags", ProviderFor(On()).GetMetadata().Name);

    [Fact]
    public async Task ResolveBoolean_ShouldCarryTheVariantAndReasonThrough()
    {
        var details = await ProviderFor(
                new FlagResolution(FlagValue.True, FlagVariantNames.On, EvaluationReason.TargetingMatch))
            .ResolveBooleanValueAsync("new-checkout", defaultValue: false,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(details.Value);
        Assert.Equal(FlagVariantNames.On, details.Variant);
        Assert.Equal(EvaluationReason.TargetingMatch, details.Reason);
        Assert.Equal(ErrorType.None, details.ErrorType);
    }

    [Fact]
    public async Task ResolveBoolean_ForATargetedFlagThatMatchedNothing_ShouldNotBeAnError()
    {
        // The reason mapping that matters most. DEFAULT is a normal answer, so nothing alerting on
        // ErrorType sees a deliberately narrowed flag as an outage.
        var details = await ProviderFor(
                new FlagResolution(FlagValue.False, FlagVariantNames.Off, EvaluationReason.Default))
            .ResolveBooleanValueAsync("new-checkout", defaultValue: true,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(details.Value);
        Assert.Equal(ErrorType.None, details.ErrorType);
        Assert.Equal(EvaluationReason.Default, details.Reason);
    }

    [Fact]
    public async Task ResolveBoolean_ForAnUnknownFlag_ShouldReturnTheCallersDefault()
    {
        // The whole reason the resolution surface exists: a bare boolean could not tell "off" from
        // "no such flag", so the caller's own default could never be honoured.
        var details = await ProviderFor(new FlagResolution(
                FlagValue.False, null, EvaluationReason.Error, EvaluationErrorCode.FlagNotFound, "nope"))
            .ResolveBooleanValueAsync("never-defined", defaultValue: true,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(details.Value);
        Assert.Equal(ErrorType.FlagNotFound, details.ErrorType);
        Assert.Equal(EvaluationReason.Error, details.Reason);
    }

    [Fact]
    public async Task ResolveBoolean_BeforeAnythingHasLoaded_ShouldSayProviderNotReady()
    {
        var details = await ProviderFor(new FlagResolution(
                FlagValue.False, null, EvaluationReason.Error, EvaluationErrorCode.ProviderNotReady, "nope"))
            .ResolveBooleanValueAsync("new-checkout", defaultValue: true,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(details.Value);
        Assert.Equal(ErrorType.ProviderNotReady, details.ErrorType);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("integer")]
    [InlineData("double")]
    [InlineData("structure")]
    public async Task ResolveNonBoolean_ShouldBeATypeMismatchRatherThanACoercedValue(string kind)
    {
        // Every flag this platform can author is boolean, so a caller asking for a string is asking
        // for something that does not exist. Inventing one from a boolean would be worse than the
        // honest refusal.
        var provider = ProviderFor(On());

        ErrorType errorType = kind switch
        {
            "string" => (await provider.ResolveStringValueAsync(
                "f", "fallback", cancellationToken: TestContext.Current.CancellationToken)).ErrorType,
            "integer" => (await provider.ResolveIntegerValueAsync(
                "f", 7, cancellationToken: TestContext.Current.CancellationToken)).ErrorType,
            "double" => (await provider.ResolveDoubleValueAsync(
                "f", 1.5, cancellationToken: TestContext.Current.CancellationToken)).ErrorType,
            _ => (await provider.ResolveStructureValueAsync(
                "f", new Value("fallback"), cancellationToken: TestContext.Current.CancellationToken)).ErrorType,
        };

        Assert.Equal(ErrorType.TypeMismatch, errorType);
    }

    [Fact]
    public async Task ResolveString_ShouldReturnTheCallersDefault()
    {
        var details = await ProviderFor(On()).ResolveStringValueAsync(
            "f", "fallback", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("fallback", details.Value);
    }

    [Fact]
    public async Task ResolveNonBoolean_ForAnUnknownFlag_ShouldReportItMissingRatherThanMismatched()
    {
        // A misspelled key is a different mistake from asking for the wrong type, and a caller can
        // only fix the one they are told about.
        var details = await ProviderFor(new FlagResolution(
                FlagValue.False, null, EvaluationReason.Error, EvaluationErrorCode.FlagNotFound, "nope"))
            .ResolveStringValueAsync("never-defined", "fallback",
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ErrorType.FlagNotFound, details.ErrorType);
    }

    [Fact]
    public void ToFlagContext_ShouldReadTheTargetingKey()
    {
        var context = FutureFlagsProvider.ToFlagContext(
            EvaluationContext.Builder().SetTargetingKey("user-17").Build());

        Assert.Equal("user-17", context.Key);
        Assert.Empty(context.Attributes);
    }

    [Fact]
    public void ToFlagContext_ShouldCarryTheThreeKindsThisPlatformHolds()
    {
        var context = FutureFlagsProvider.ToFlagContext(EvaluationContext.Builder()
            .SetTargetingKey("user-17")
            .Set("plan", "enterprise")
            .Set("age", 30)
            .Set("beta", true)
            .Build());

        Assert.True(context.TryGetAttribute("plan", out var plan));
        Assert.Equal(AttributeValue.OfText("enterprise"), plan);

        Assert.True(context.TryGetAttribute("age", out var age));
        Assert.Equal(AttributeValue.OfNumber(30), age);

        Assert.True(context.TryGetAttribute("beta", out var beta));
        Assert.Equal(AttributeValue.OfBoolean(true), beta);
    }

    [Fact]
    public void ToFlagContext_ShouldDropAStructureRatherThanFail()
    {
        // Matches what the server's own OFREP routes do with the same context: absent, and absent
        // never matches. Failing would mean one unrelated field stops every flag resolving.
        var context = FutureFlagsProvider.ToFlagContext(EvaluationContext.Builder()
            .SetTargetingKey("user-17")
            .Set("plan", "enterprise")
            .Set("nested", new Value(Structure.Builder().Set("a", 1).Build()))
            .Build());

        Assert.True(context.TryGetAttribute("plan", out _));
        Assert.False(context.TryGetAttribute("nested", out _));
    }

    [Fact]
    public void ToFlagContext_ShouldRenderADatetimeAsText()
    {
        // Millisecond precision and a literal "Z" — matching Node's Date.toISOString(), which is
        // what the OFREP route and the Node providers render the same field with. DateTime's own
        // "O" format uses seven fractional digits and would not agree.
        var when = new DateTime(2026, 2, 20, 21, 28, 18, 123, DateTimeKind.Utc);

        var context = FutureFlagsProvider.ToFlagContext(EvaluationContext.Builder()
            .Set("signedUpAt", when)
            .Build());

        Assert.True(context.TryGetAttribute("signedUpAt", out var value));
        Assert.Equal(AttributeValueKind.Text, value.Kind);
        Assert.Equal("2026-02-20T21:28:18.123Z", value.Text);
    }

    [Fact]
    public void ToFlagContext_ShouldTreatAnUnspecifiedKindDatetimeAsUtc()
    {
        // A caller that builds a DateTime without a Kind is the common case, and treating it as
        // Utc (rather than converting as if it were Local) is what keeps this deterministic across
        // machines in different time zones.
        var when = new DateTime(2026, 2, 20, 21, 28, 18, 123, DateTimeKind.Unspecified);

        var context = FutureFlagsProvider.ToFlagContext(EvaluationContext.Builder()
            .Set("signedUpAt", when)
            .Build());

        Assert.True(context.TryGetAttribute("signedUpAt", out var value));
        Assert.Equal("2026-02-20T21:28:18.123Z", value.Text);
    }

    [Fact]
    public void ToFlagContext_WithNothing_ShouldBeTheEmptyContext()
    {
        var context = FutureFlagsProvider.ToFlagContext(null);

        Assert.Null(context.Key);
        Assert.Empty(context.Attributes);
    }

    [Fact]
    public async Task ResolveBoolean_ShouldPassTheContextThrough()
    {
        var client = new StubClient(On());

        await new FutureFlagsProvider(client).ResolveBooleanValueAsync(
            "f",
            defaultValue: false,
            EvaluationContext.Builder().SetTargetingKey("user-17").Build(),
            TestContext.Current.CancellationToken);

        Assert.Equal("user-17", client.LastContext?.Key);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("integer")]
    [InlineData("double")]
    [InlineData("structure")]
    public async Task ResolveNonBoolean_ShouldPassTheContextThrough(string kind)
    {
        // These overloads answer TYPE_MISMATCH today because every flag this platform can author is
        // boolean, but the lookup itself must still run for the caller's own context — resolving for
        // FlagContext.Empty instead would evaluate every caller as anonymous the day a targeted
        // non-boolean flag exists.
        var client = new StubClient(On());
        var provider = new FutureFlagsProvider(client);
        var context = EvaluationContext.Builder().SetTargetingKey("user-17").Build();

        switch (kind)
        {
            case "string":
                await provider.ResolveStringValueAsync(
                    "f", "fallback", context, TestContext.Current.CancellationToken);
                break;
            case "integer":
                await provider.ResolveIntegerValueAsync(
                    "f", 7, context, TestContext.Current.CancellationToken);
                break;
            case "double":
                await provider.ResolveDoubleValueAsync(
                    "f", 1.5, context, TestContext.Current.CancellationToken);
                break;
            default:
                await provider.ResolveStructureValueAsync(
                    "f", new Value("fallback"), context, TestContext.Current.CancellationToken);
                break;
        }

        Assert.Equal("user-17", client.LastContext?.Key);
    }

    [Theory]
    [InlineData("wrapped")]
    [InlineData("connection")]
    [InlineData("timeout")]
    public async Task Initialize_WhenRefreshCannotReachTheServer_ShouldNotThrow(string failure)
    {
        // The specification requires a flag evaluation never to throw, and this client's posture is
        // that an unreachable service is not a reason to fail initialization — every resolution says
        // PROVIDER_NOT_READY instead. HttpRequestException (a connection that never opens) and a bare
        // TaskCanceledException (this client's own request timeout, not caller cancellation) are the
        // same "unreachable" case as FutureFlagsException, just thrown from a different layer.
        Exception exception = failure switch
        {
            "wrapped" => new FutureFlagsException("unreachable"),
            "connection" => new HttpRequestException("connection refused"),
            _ => new TaskCanceledException(),
        };
        var client = new StubClient(On()) { RefreshFailure = exception };

        await new FutureFlagsProvider(client).InitializeAsync(
            EvaluationContext.Empty, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Initialize_WhenTheCallerCancels_ShouldStillThrow()
    {
        // The one case that must not be absorbed: the caller's own cancellation is an instruction,
        // not a server failure, and swallowing it would leave them waiting on a token they cancelled.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var client = new StubClient(On()) { RefreshFailure = new OperationCanceledException(cts.Token) };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new FutureFlagsProvider(client).InitializeAsync(EvaluationContext.Empty, cts.Token));
    }

    private static FlagResolution On() =>
        new(FlagValue.True, FlagVariantNames.On, EvaluationReason.Static);
}
