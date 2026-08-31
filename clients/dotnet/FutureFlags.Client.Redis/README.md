# FutureFlags.Client.Redis

An optional Redis cache tier for [`FutureFlags.Client`](https://www.nuget.org/packages/FutureFlags.Client),
built on [FusionCache](https://github.com/ZiggyCreatures/FusionCache). Backs the client's in-memory
snapshot with Redis your own application already runs, so:

- a freshly started (or newly scaled-up) instance answers correctly from Redis on its first read,
  instead of serving defaults until its first successful call to the FutureFlags server, and
- an outage of the FutureFlags server is survived for as long as Redis holds a stale-but-usable
  answer, not just for as long as one process happens to keep its own in-memory snapshot warm.

```sh
dotnet add package FutureFlags.Client.Redis
```

## Use

```csharp
builder.Services.AddFutureFlags(options =>
{
    options.BaseAddress = new Uri("https://flags.example.com");
    options.SdkKey = builder.Configuration["FutureFlags:SdkKey"];
});

builder.Services.AddFutureFlagsRedisCache();
```

Call it after `AddFutureFlags` — it replaces the `IFutureFlagsClient` that call registered, and
throws at startup if `AddFutureFlags` was never called, rather than failing at the first read.

**This is your own Redis, not the FutureFlags server's.** By default it is read from whatever
`IConnectionMultiplexer` your application already has registered — the Aspire client integration
(`builder.AddRedisClient("cache")`) does this for you, so an Aspire-orchestrated consumer needs
nothing further. Without one registered, or to point at a different connection, set
`ConnectionMultiplexerFactory` explicitly:

```csharp
builder.Services.AddFutureFlagsRedisCache(redis =>
{
    redis.ConnectionMultiplexerFactory = provider => ConnectionMultiplexer.Connect("localhost:6379");
});
```

## How it behaves

**A read still costs a dictionary lookup, not a request** — same as the base package. What's
different is what backs it once the in-memory entry expires: FusionCache checks Redis before ever
asking the FutureFlags server, so a value another instance of your application already fetched is
reused rather than every instance polling the origin independently.

**Two different numbers govern staleness, on purpose.** `FutureFlagsOptions.PollingInterval` (from
the base package, 30 seconds by default) is still the normal freshness bound — how long an answer
may go before this tier asks the origin again. `FutureFlagsRedisCacheOptions.FailSafeMaxDuration`
(24 hours by default) is the new one: how long a Redis-held answer may still be served once the
origin is genuinely unreachable. Conflating the two would mean Redis buys almost no protection over
the in-memory tier alone — keep the fail-safe window much larger than the polling interval.

**`RefreshAsync` writes through both tiers.** An explicit refresh updates Redis, not just this
process's own memory — and every other instance sharing that Redis learns about it too, without
waiting for its own `PollingInterval` to elapse.

## Options

| | | |
|---|---|---|
| `ConnectionMultiplexerFactory` | resolved from DI | Where to get the `IConnectionMultiplexer`. |
| `FailSafeMaxDuration` | 24h | How long a stale answer survives a real outage. |
| `FailSafeThrottleDuration` | 30s | How often fail-safe retries the origin during a sustained outage. |
| `EagerRefreshThreshold` | 0.5 | Fraction of `PollingInterval` after which a read triggers a background refresh instead of waiting for the entry to fully expire. |
| `KeyPrefix` | `"futureflags:"` | Prefixed onto the Redis key, so it cannot collide with your application's own keys. The key already includes the installation's host and the SDK key's environment, so two environments — or two installations — sharing one Redis and the same `KeyPrefix` still don't collide with each other. |

## Versioning

Ships on the same `sdk-dotnet-v*` tag as `FutureFlags.Client` — it references that package by
project reference, so a version bump in one is a version bump in both.
