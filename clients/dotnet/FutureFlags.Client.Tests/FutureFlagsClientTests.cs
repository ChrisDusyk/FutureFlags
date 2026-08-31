using System.Net;
using FutureFlags.Client.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FutureFlags.Client.Tests;

public class FutureFlagsClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly StubHandler _server = new();
    private readonly FakeTimeProvider _timeProvider = new(Now);

    private readonly FutureFlagsOptions _options = new()
    {
        BaseAddress = new Uri("https://flags.example.com"),
        SdkKey = "ffs_dev_f992c8928754087a_7f097037aa14d671f4317df877989f05f5309c1323ecb24dab4be5597f40db10",
        PollingInterval = TimeSpan.FromSeconds(30)
    };

    private FutureFlagsClient CreateSut()
    {
        var http = new HttpClient(_server) { BaseAddress = new Uri("https://flags.example.com/") };

        return new FutureFlagsClient(
            new EvaluationApiClient(http),
            Options.Create(_options),
            NullLogger<FutureFlagsClient>.Instance,
            _timeProvider);
    }

    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task IsEnabledAsync_ShouldReadTheFlagTheServerReported()
    {
        _server.AnswersWithFlags("dev", new { new_checkout = true, dark_mode = false }, "\"v1\"");

        var client = CreateSut();

        Assert.True(await client.IsEnabledAsync("new_checkout", Cancellation));
        Assert.False(await client.IsEnabledAsync("dark_mode", Cancellation));
    }

    [Fact]
    public async Task IsEnabledAsync_ShouldNotAskAgainWithinThePollingInterval()
    {
        _server.AnswersWithFlags("dev", new { on = true }, "\"v1\"");

        var client = CreateSut();

        for (var i = 0; i < 10; i++)
        {
            await client.IsEnabledAsync("on", Cancellation);
        }

        // The whole point of the snapshot: reads are a dictionary lookup, not a request.
        Assert.Equal(1, _server.CallCount);
    }

    [Fact]
    public async Task IsEnabledAsync_PastThePollingInterval_ShouldRefetch()
    {
        _server.AnswersWithFlags("dev", new { on = true }, "\"v1\"");
        _server.AnswersWithFlags("dev", new { on = false }, "\"v2\"");

        var client = CreateSut();

        Assert.True(await client.IsEnabledAsync("on", Cancellation));

        _timeProvider.Advance(_options.PollingInterval + TimeSpan.FromSeconds(1));

        Assert.False(await client.IsEnabledAsync("on", Cancellation));
        Assert.Equal(2, _server.CallCount);
    }

    [Fact]
    public async Task IsEnabledAsync_WithAnUnknownKey_ShouldBeFalse()
    {
        _server.AnswersWithFlags("dev", new { on = true }, "\"v1\"");

        Assert.False(await CreateSut().IsEnabledAsync("never-heard-of-it", Cancellation));
    }

    [Fact]
    public async Task IsEnabledAsync_WithAnUnknownKeyAndADefault_ShouldUseTheDefault()
    {
        _server.AnswersWithFlags("dev", new { on = true }, "\"v1\"");

        Assert.True(await CreateSut().IsEnabledAsync("never-heard-of-it", true, Cancellation));
    }

    [Fact]
    public async Task IsEnabledAsync_ShouldNotCareAboutTheKeysCasing()
    {
        _server.AnswersWithFlags("dev", new { newcheckout = true }, "\"v1\"");

        Assert.True(await CreateSut().IsEnabledAsync("NewCheckout", Cancellation));
    }

    /// <summary>
    /// The behaviour this package is most likely to be judged on. A flag service that cannot be
    /// reached must not become an outage in everything that reads it.
    /// </summary>
    [Fact]
    public async Task IsEnabledAsync_WhenTheServerIsUnreachable_ShouldReturnTheDefaultRatherThanThrow()
    {
        _server.Throws();

        var client = CreateSut();

        Assert.False(await client.IsEnabledAsync("anything", Cancellation));
        Assert.True(await client.IsEnabledAsync("anything", true, Cancellation));
    }

    [Fact]
    public async Task IsEnabledAsync_WhenARefreshFails_ShouldKeepTheLastGoodSnapshot()
    {
        _server.AnswersWithFlags("dev", new { on = true }, "\"v1\"");
        _server.AnswersWithStatus(HttpStatusCode.InternalServerError);

        var client = CreateSut();

        Assert.True(await client.IsEnabledAsync("on", Cancellation));

        _timeProvider.Advance(_options.PollingInterval + TimeSpan.FromSeconds(1));

        // The refresh failed. The answer from before it is still the best one available.
        Assert.True(await client.IsEnabledAsync("on", Cancellation));
    }

    /// <summary>
    /// A server that accepts the connection and then never answers is the failure mode a bare
    /// "unreachable" test misses: the timeout surfaces as an OperationCanceledException, the same
    /// type a caller's own cancellation uses, and the two have to be told apart.
    /// </summary>
    [Fact]
    public async Task IsEnabledAsync_WhenTheServerHangs_ShouldReturnTheDefaultRatherThanThrow()
    {
        _options.Timeout = TimeSpan.FromMilliseconds(50);
        _server.Delay = TimeSpan.FromSeconds(30);
        _server.AnswersWithFlags("dev", new { on = true }, "\"v1\"");

        Assert.False(await CreateSut().IsEnabledAsync("on", Cancellation));
    }

    [Fact]
    public async Task IsEnabledAsync_WhenTheCallerCancels_ShouldNotSwallowIt()
    {
        _server.Delay = TimeSpan.FromSeconds(30);
        _server.AnswersWithFlags("dev", new { on = true }, "\"v1\"");

        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // The caller's own instruction, not a failure to absorb — swallowing it would leave them
        // waiting on a token they already cancelled.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateSut().IsEnabledAsync("on", caller.Token));
    }

    [Fact]
    public async Task RefreshAsync_WhenTheServerHangs_ShouldReportIt()
    {
        _options.Timeout = TimeSpan.FromMilliseconds(50);
        _server.Delay = TimeSpan.FromSeconds(30);
        _server.AnswersWithFlags("dev", new { on = true }, "\"v1\"");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateSut().RefreshAsync(Cancellation));
    }

    [Fact]
    public async Task RefreshAsync_ShouldReportAFailureRatherThanSwallowIt()
    {
        _server.AnswersWithStatus(HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<FutureFlagsException>(() => CreateSut().RefreshAsync(Cancellation));
    }

    [Fact]
    public async Task RefreshAsync_WithARejectedKey_ShouldSaySo()
    {
        _server.AnswersWithStatus(HttpStatusCode.Unauthorized);

        var exception = await Assert.ThrowsAsync<FutureFlagsException>(
            () => CreateSut().RefreshAsync(Cancellation));

        Assert.Contains("rejected this SDK key", exception.Message);
    }

    [Fact]
    public async Task RefreshAsync_ShouldFetchEvenWhenTheSnapshotIsFresh()
    {
        _server.AnswersWithFlags("dev", new { on = true }, "\"v1\"");
        _server.AnswersWithFlags("dev", new { on = false }, "\"v2\"");

        var client = CreateSut();

        await client.IsEnabledAsync("on", Cancellation);
        await client.RefreshAsync(Cancellation);

        Assert.False(await client.IsEnabledAsync("on", Cancellation));
        Assert.Equal(2, _server.CallCount);
    }

    [Fact]
    public async Task ConcurrentReads_ShouldProduceOneRequest()
    {
        _server.AnswersWithFlags("dev", new { on = true }, "\"v1\"");

        var client = CreateSut();

        await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => client.IsEnabledAsync("on", Cancellation)));

        // Twenty readers finding an empty snapshot at once is one request, not twenty.
        Assert.Equal(1, _server.CallCount);
    }

    [Fact]
    public async Task IsEnabledAsync_WithANullKey_ShouldThrow()
    {
        _server.AnswersWithFlags("dev", new { on = true }, "\"v1\"");

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => CreateSut().IsEnabledAsync(null!, Cancellation));
    }

    [Fact]
    public async Task IsEnabledAsync_WithNonsenseFromTheServer_ShouldFallBackRatherThanThrow()
    {
        _server.Answers(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            // What a proxy or a login page in front of the API would answer with.
            Content = new StringContent("<!doctype html><html>…", System.Text.Encoding.UTF8, "text/html")
        });

        Assert.False(await CreateSut().IsEnabledAsync("on", Cancellation));
    }
}
