using System;
using StackExchange.Redis;

namespace FutureFlags.Client.Redis;

/// <summary>
/// How the Redis cache tier behaves, on top of whatever <see cref="Client.FutureFlagsOptions"/>
/// already says about reaching the FutureFlags server.
/// </summary>
public sealed class FutureFlagsRedisCacheOptions
{
    /// <summary>
    /// Where to get the <see cref="IConnectionMultiplexer"/> from. Left unset, it is resolved from
    /// this application's own <c>IServiceCollection</c> — the Redis this add-on caches through is
    /// the one the host application already runs, not one this package provisions or connects to
    /// on its own. Set this only for the uncommon case of wanting a second, separate connection.
    /// </summary>
    public Func<IServiceProvider, IConnectionMultiplexer>? ConnectionMultiplexerFactory { get; set; }

    /// <summary>
    /// How long a Redis-cached answer may still be served after it would normally have expired,
    /// once the FutureFlags server cannot be reached at all. This is the number that actually
    /// matters for surviving an outage — keep it long. Defaults to 24 hours.
    /// </summary>
    public TimeSpan FailSafeMaxDuration { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Once fail-safe has served a stale answer, how long before this process tries the origin
    /// again, rather than retrying on every single read of a sustained outage. Defaults to 30
    /// seconds.
    /// </summary>
    public TimeSpan FailSafeThrottleDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The fraction of <see cref="Client.FutureFlagsOptions.PollingInterval"/> after which a read
    /// triggers a non-blocking background refresh instead of waiting for the entry to fully expire.
    /// 0 disables eager refresh. Defaults to 0.5 — refresh in the background once an entry is half
    /// as old as it is allowed to get, so a read essentially never waits on the network once warm.
    /// </summary>
    public float EagerRefreshThreshold { get; set; } = 0.5f;

    /// <summary>
    /// Prefixed onto the Redis key this add-on uses, so it cannot collide with unrelated keys in a
    /// Redis instance the host application also uses for its own caching. Defaults to
    /// <c>"futureflags:"</c>.
    /// </summary>
    public string KeyPrefix { get; set; } = "futureflags:";
}
