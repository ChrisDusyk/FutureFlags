using FutureFlags.Client.Internal;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace FutureFlags.Client.Redis.Tests;

/// <summary>
/// Against a real Redis (one Testcontainers instance for the whole collection — see
/// <see cref="RedisFixture"/>): the behaviors that only exist once there is an actual L2 and a real
/// backplane behind the in-memory tier, which nothing about the base package's own tests can cover.
/// </summary>
[Collection(nameof(RedisCollection))]
public sealed class RedisCachedFutureFlagsClientTests(RedisFixture redis)
{
    private const string SdkKey =
        "ffs_dev_f992c8928754087a_7f097037aa14d671f4317df877989f05f5309c1323ecb24dab4be5597f40db10";

    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // A dedicated connection per cache instance, not the fixture sharing one: RedisCache.Dispose()
    // closes and disposes whatever IConnectionMultiplexer it is given regardless of who created it,
    // so a shared multiplexer gets pulled out from under any test still running once an earlier
    // test's FusionCache/RedisCache is garbage collected and finalized.
    private IFusionCache BuildCache()
    {
        var multiplexer = ConnectionMultiplexer.Connect(redis.ConnectionString);

        return new FusionCache(Options.Create(new FusionCacheOptions()))
            .SetupSerializer(new FusionCacheSystemTextJsonSerializer())
            .SetupDistributedCache(new RedisCache(new RedisCacheOptions
            {
                ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(multiplexer)
            }))
            .SetupBackplane(new RedisBackplane(new RedisBackplaneOptions
            {
                ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(multiplexer)
            }));
    }

    private RedisCachedFutureFlagsClient CreateSut(
        StubHandler server,
        string keyPrefix,
        TimeSpan? pollingInterval = null,
        TimeSpan? failSafeMaxDuration = null,
        string? sdkKey = null,
        Uri? baseAddress = null) =>
        new(
            new EvaluationApiClient(new HttpClient(server) { BaseAddress = baseAddress ?? new Uri("https://flags.example.com/") }),
            BuildCache(),
            Options.Create(new FutureFlagsOptions
            {
                BaseAddress = baseAddress ?? new Uri("https://flags.example.com"),
                SdkKey = sdkKey ?? SdkKey,
                PollingInterval = pollingInterval ?? TimeSpan.FromMilliseconds(200)
            }),
            new FutureFlagsRedisCacheOptions
            {
                KeyPrefix = keyPrefix,
                FailSafeMaxDuration = failSafeMaxDuration ?? TimeSpan.FromHours(24),
                FailSafeThrottleDuration = TimeSpan.FromMilliseconds(50)
            },
            TimeProvider.System);

    [Fact]
    public async Task WhenTheOriginFails_AStaleRedisValue_IsStillServed()
    {
        var server = new StubHandler().AnswersWithFlags("dev", new { on = true }, "\"v1\"");
        var sut = CreateSut(server, RedisFixture.NewKeyPrefix());

        Assert.True(await sut.IsEnabledAsync("on", Cancellation));

        // The origin is gone, and the entry's 200ms Duration has elapsed — a healthy read would
        // refetch and get nothing to work with. Fail-safe should answer from Redis instead.
        server.Throws();
        await Task.Delay(400, Cancellation);

        Assert.True(await sut.IsEnabledAsync("on", defaultValue: false, Cancellation));
    }

    [Fact]
    public async Task ASecondColdInstance_ReadsTheFirstInstancesValue_WithoutCallingTheOrigin()
    {
        var keyPrefix = RedisFixture.NewKeyPrefix();

        var firstServer = new StubHandler().AnswersWithFlags("dev", new { on = true }, "\"v1\"");
        var first = CreateSut(firstServer, keyPrefix, pollingInterval: TimeSpan.FromMinutes(1));
        Assert.True(await first.IsEnabledAsync("on", Cancellation));

        // A second instance, its own FusionCache and own empty L1, pointed at the same Redis. If it
        // reads Redis before ever asking the origin, this server — which refuses every request —
        // never gets called.
        var secondServer = new StubHandler().Throws();
        var second = CreateSut(secondServer, keyPrefix, pollingInterval: TimeSpan.FromMinutes(1));

        Assert.True(await second.IsEnabledAsync("on", Cancellation));
        Assert.Equal(0, secondServer.CallCount);
    }

    [Fact]
    public async Task RefreshAsync_ThrowsOnFailure_MatchingTheInterfaceContract()
    {
        var server = new StubHandler().Throws();
        var sut = CreateSut(server, RedisFixture.NewKeyPrefix());

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.RefreshAsync(Cancellation));
    }

    [Fact]
    public async Task RefreshAsync_PublishesToOtherInstancesSharingTheBackplane()
    {
        var keyPrefix = RedisFixture.NewKeyPrefix();

        var firstServer = new StubHandler().AnswersWithFlags("dev", new { on = true }, "\"v1\"");
        var first = CreateSut(firstServer, keyPrefix, pollingInterval: TimeSpan.FromMinutes(1));

        var secondServer = new StubHandler().AnswersWithFlags("dev", new { on = true }, "\"v1\"");
        var second = CreateSut(secondServer, keyPrefix, pollingInterval: TimeSpan.FromMinutes(1));

        Assert.True(await first.IsEnabledAsync("on", Cancellation));
        Assert.True(await second.IsEnabledAsync("on", Cancellation));

        var callsBeforeTheFlip = secondServer.CallCount;

        // Only the first instance is told about the flip. The second should learn of it through the
        // backplane, not by outliving its own minute-long Duration or asking its own origin again.
        firstServer.AnswersWithFlags("dev", new { on = false }, "\"v2\"");
        await first.RefreshAsync(Cancellation);

        var flipped = false;

        for (var i = 0; i < 30 && !flipped; i++)
        {
            await Task.Delay(100, Cancellation);
            flipped = !await second.IsEnabledAsync("on", Cancellation);
        }

        Assert.True(flipped);
        Assert.Equal(callsBeforeTheFlip, secondServer.CallCount);
    }

    [Fact]
    public async Task TwoEnvironmentsSharingOneKeyPrefix_DoNotOverwriteEachOthersSnapshot()
    {
        var keyPrefix = RedisFixture.NewKeyPrefix();
        const string devKey = "ffs_dev_f992c8928754087a_7f097037aa14d671f4317df877989f05f5309c1323ecb24dab4be5597f40db10";
        const string prodKey = "ffs_prod_f992c8928754087a_7f097037aa14d671f4317df877989f05f5309c1323ecb24dab4be5597f40db10";

        var dev = CreateSut(
            new StubHandler().AnswersWithFlags("dev", new { on = true }, "\"v1\""),
            keyPrefix,
            sdkKey: devKey);
        var prod = CreateSut(
            new StubHandler().AnswersWithFlags("prod", new { on = false }, "\"v1\""),
            keyPrefix,
            sdkKey: prodKey);

        Assert.True(await dev.IsEnabledAsync("on", Cancellation));
        Assert.False(await prod.IsEnabledAsync("on", Cancellation));

        // Reading dev again afterwards should still be true — if the two shared one Redis key,
        // whichever answered second would have clobbered the other's entry.
        Assert.True(await dev.IsEnabledAsync("on", Cancellation));
    }

    [Fact]
    public async Task TwoInstallationsSharingOneKeyPrefixAndEnvironment_DoNotOverwriteEachOthersSnapshot()
    {
        var keyPrefix = RedisFixture.NewKeyPrefix();

        var a = CreateSut(
            new StubHandler().AnswersWithFlags("dev", new { on = true }, "\"v1\""),
            keyPrefix,
            baseAddress: new Uri("https://flags-a.example.com/"));
        var b = CreateSut(
            new StubHandler().AnswersWithFlags("dev", new { on = false }, "\"v1\""),
            keyPrefix,
            baseAddress: new Uri("https://flags-b.example.com/"));

        Assert.True(await a.IsEnabledAsync("on", Cancellation));
        Assert.False(await b.IsEnabledAsync("on", Cancellation));
        Assert.True(await a.IsEnabledAsync("on", Cancellation));
    }

    [Fact]
    public async Task TwoInstallationsOnTheSameHostButDifferentPorts_DoNotOverwriteEachOthersSnapshot()
    {
        var keyPrefix = RedisFixture.NewKeyPrefix();

        // Same host, different port — the common shape for two local instances on one machine.
        // Uri.Host alone would collide these under the same cache key.
        var a = CreateSut(
            new StubHandler().AnswersWithFlags("dev", new { on = true }, "\"v1\""),
            keyPrefix,
            baseAddress: new Uri("https://flags.example.com:5001/"));
        var b = CreateSut(
            new StubHandler().AnswersWithFlags("dev", new { on = false }, "\"v1\""),
            keyPrefix,
            baseAddress: new Uri("https://flags.example.com:5002/"));

        Assert.True(await a.IsEnabledAsync("on", Cancellation));
        Assert.False(await b.IsEnabledAsync("on", Cancellation));
        Assert.True(await a.IsEnabledAsync("on", Cancellation));
    }

    [Fact]
    public async Task ANotModifiedResponse_ShouldKeepThePreviousAnswer()
    {
        var server = new StubHandler().AnswersWithFlags("dev", new { on = true }, "\"v1\"");
        var sut = CreateSut(server, RedisFixture.NewKeyPrefix());

        Assert.True(await sut.IsEnabledAsync("on", Cancellation));

        server.AnswersNotModified("\"v1\"");
        await Task.Delay(400, Cancellation);

        Assert.True(await sut.IsEnabledAsync("on", Cancellation));
    }
}
