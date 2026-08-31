using System;
using System.Linq;
using System.Threading.Tasks;
using FutureFlags.Client.Internal;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace FutureFlags.Client.Redis;

/// <summary>
/// Layers a Redis cache tier onto a client already registered by <c>AddFutureFlags</c>.
///
/// <code>
/// services.AddFutureFlags(options => { ... });
/// services.AddFutureFlagsRedisCache(); // resolves IConnectionMultiplexer from this container
/// </code>
/// </summary>
public static class FutureFlagsRedisCacheServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Redis tier with default options, reading <see cref="IConnectionMultiplexer"/> from
    /// this application's own <c>IServiceCollection</c>.
    /// </summary>
    public static IServiceCollection AddFutureFlagsRedisCache(this IServiceCollection services) =>
        AddFutureFlagsRedisCache(services, static _ => { });

    /// <summary>Adds the Redis tier, configured in code.</summary>
    public static IServiceCollection AddFutureFlagsRedisCache(
        this IServiceCollection services,
        Action<FutureFlagsRedisCacheOptions> configure)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        // AddFutureFlags registers EvaluationApiClient as a typed HttpClient. Requiring it first,
        // loudly, beats a NullReferenceException the first time something reads a flag — the whole
        // point of this check is to fail at startup instead of at a request.
        if (services.All(descriptor => descriptor.ServiceType != typeof(EvaluationApiClient)))
        {
            throw new InvalidOperationException(
                "AddFutureFlagsRedisCache requires AddFutureFlags to be called first — it reuses " +
                "the HTTP client and options that call registers.");
        }

        var redisOptions = new FutureFlagsRedisCacheOptions();
        configure(redisOptions);
        Validate(redisOptions);

        // One resolver instance shared by both callbacks below: WithDistributedCache and
        // WithBackplane each own their ConnectionMultiplexerFactory and call it independently, so
        // without memoizing, a caller-supplied ConnectionMultiplexerFactory that opens a new
        // connection (the shape the README shows) would open two — one nothing else here needs.
        var resolveMultiplexer = CreateMultiplexerResolver(redisOptions);

        services
            .AddFusionCache()
            .WithSerializer(new FusionCacheSystemTextJsonSerializer())
            .WithDistributedCache(provider => new RedisCache(new RedisCacheOptions
            {
                ConnectionMultiplexerFactory = () => Task.FromResult(resolveMultiplexer(provider))
            }))
            .WithBackplane(provider => new RedisBackplane(new RedisBackplaneOptions
            {
                ConnectionMultiplexerFactory = () => Task.FromResult(resolveMultiplexer(provider))
            }));

        // Not TryAdd: this is meant to replace the in-memory-only client AddFutureFlags registered.
        // The last registration wins when IFutureFlagsClient is resolved singly, which is every
        // caller of it — FutureFlagsRefreshService included, so the same background polling loop
        // keeps this tier warm too, it just refreshes through Redis instead of only in memory.
        services.AddSingleton<IFutureFlagsClient>(provider => new RedisCachedFutureFlagsClient(
            provider.GetRequiredService<EvaluationApiClient>(),
            provider.GetRequiredService<IFusionCache>(),
            provider.GetRequiredService<IOptions<FutureFlagsOptions>>(),
            redisOptions,
            provider.GetRequiredService<TimeProvider>()));

        return services;
    }

    private static Func<IServiceProvider, IConnectionMultiplexer> CreateMultiplexerResolver(
        FutureFlagsRedisCacheOptions options)
    {
        var gate = new object();
        IConnectionMultiplexer? multiplexer = null;

        return provider =>
        {
            lock (gate)
            {
                return multiplexer ??= options.ConnectionMultiplexerFactory is { } factory
                    ? factory(provider)
                    : provider.GetRequiredService<IConnectionMultiplexer>();
            }
        };
    }

    // Options set through a plain settable POCO, not the IOptions pattern's own validation pipeline
    // — so nothing else catches a bad value before it reaches FusionCache and surfaces as a null
    // reference or silently wrong caching behavior instead of a clear message at startup.
    private static void Validate(FutureFlagsRedisCacheOptions options)
    {
        if (string.IsNullOrEmpty(options.KeyPrefix))
        {
            throw new ArgumentException("KeyPrefix must not be null or empty.", nameof(options));
        }

        if (options.FailSafeMaxDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), options.FailSafeMaxDuration, "FailSafeMaxDuration must be positive.");
        }

        if (options.FailSafeThrottleDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.FailSafeThrottleDuration,
                "FailSafeThrottleDuration must not be negative.");
        }

        if (options.EagerRefreshThreshold is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.EagerRefreshThreshold,
                "EagerRefreshThreshold must be between 0 and 1.");
        }
    }
}
