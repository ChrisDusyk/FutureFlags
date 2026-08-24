using FeatureFlags.Client.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FeatureFlags.Client.Tests;

/// <summary>
/// The ETag half. A poll that finds nothing changed should cost a 304 and no body, which only
/// works if the client sends the tag back exactly as it received it and keeps what it already had.
/// </summary>
public class ConditionalRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly StubHandler _server = new();
    private readonly FakeTimeProvider _timeProvider = new(Now);

    private readonly FeatureFlagsOptions _options = new()
    {
        BaseAddress = new Uri("https://flags.example.com"),
        SdkKey = "ffs_dev_f992c8928754087a_7f097037aa14d671f4317df877989f05f5309c1323ecb24dab4be5597f40db10",
        PollingInterval = TimeSpan.FromSeconds(30)
    };

    private FeatureFlagClient CreateSut() => new(
        new EvaluationApiClient(new HttpClient(_server) { BaseAddress = new Uri("https://flags.example.com/") }),
        Options.Create(_options),
        NullLogger<FeatureFlagClient>.Instance,
        _timeProvider);

    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheFirstRequest_ShouldNotSendIfNoneMatch()
    {
        _server.AnswersWithFlags("dev", new { on = true }, "\"v1\"");

        await CreateSut().IsEnabledAsync("on", Cancellation);

        Assert.DoesNotContain("If-None-Match", _server.Requests[0].Headers.Select(header => header.Key));
    }

    [Fact]
    public async Task ASubsequentRequest_ShouldSendTheTagItWasGiven()
    {
        _server.AnswersWithFlags("dev", new { on = true }, "\"v1\"");
        _server.AnswersNotModified("\"v1\"");

        var client = CreateSut();
        await client.IsEnabledAsync("on", Cancellation);

        _timeProvider.Advance(_options.PollingInterval + TimeSpan.FromSeconds(1));
        await client.IsEnabledAsync("on", Cancellation);

        Assert.Equal("\"v1\"", _server.Requests[1].Headers.IfNoneMatch.Single().ToString());
    }

    [Fact]
    public async Task ANotModified_ShouldKeepThePreviousAnswer()
    {
        _server.AnswersWithFlags("dev", new { on = true }, "\"v1\"");
        _server.AnswersNotModified("\"v1\"");

        var client = CreateSut();
        await client.IsEnabledAsync("on", Cancellation);

        _timeProvider.Advance(_options.PollingInterval + TimeSpan.FromSeconds(1));

        Assert.True(await client.IsEnabledAsync("on", Cancellation));
    }

    /// <summary>
    /// Without re-stamping the snapshot's age on a 304, an unchanged answer would look stale
    /// forever and be refetched on every single read.
    /// </summary>
    [Fact]
    public async Task ANotModified_ShouldResetTheSnapshotsAge()
    {
        _server.AnswersWithFlags("dev", new { on = true }, "\"v1\"");
        _server.AnswersNotModified("\"v1\"");

        var client = CreateSut();
        await client.IsEnabledAsync("on", Cancellation);

        _timeProvider.Advance(_options.PollingInterval + TimeSpan.FromSeconds(1));
        await client.IsEnabledAsync("on", Cancellation);

        var afterTheRefresh = _server.CallCount;

        for (var i = 0; i < 5; i++)
        {
            await client.IsEnabledAsync("on", Cancellation);
        }

        Assert.Equal(afterTheRefresh, _server.CallCount);
    }

    [Fact]
    public async Task EveryRequest_ShouldCarryTheSdkKeyAndAskForJson()
    {
        _server.AnswersWithFlags("dev", new { on = true }, "\"v1\"");

        // Set on the HttpClient by AddFeatureFlags in the real thing; asserted here on the request
        // this test's own HttpClient produces, so the header names and the path stay honest.
        var http = new HttpClient(_server) { BaseAddress = new Uri("https://flags.example.com/") };
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.SdkKey);

        await new EvaluationApiClient(http).FetchAsync(null, Now, Cancellation);

        var request = _server.Requests.Single();

        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(_options.SdkKey, request.Headers.Authorization?.Parameter);
        Assert.Equal("https://flags.example.com/api/evaluation/ruleset", request.RequestUri?.ToString());
    }

    /// <summary>
    /// An installation served under a sub-path is the case a missing trailing slash breaks: Uri
    /// composition drops the last segment, and the request quietly goes to the wrong place.
    /// </summary>
    [Fact]
    public async Task ABaseAddressWithAPath_ShouldKeepIt()
    {
        _server.AnswersWithFlags("dev", new { on = true }, "\"v1\"");

        var http = new HttpClient(_server) { BaseAddress = new Uri("https://example.com/flags/") };

        await new EvaluationApiClient(http).FetchAsync(null, Now, Cancellation);

        Assert.Equal("https://example.com/flags/api/evaluation/ruleset", _server.Requests.Single().RequestUri?.ToString());
    }
}
