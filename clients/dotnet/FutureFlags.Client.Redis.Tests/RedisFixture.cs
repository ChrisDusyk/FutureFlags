using Testcontainers.Redis;

namespace FutureFlags.Client.Redis.Tests;

/// <summary>
/// One Redis container for the whole collection, not one per test: starting a container is the
/// slow part, and these tests isolate from each other with a unique key prefix
/// (<see cref="NewKeyPrefix"/>) rather than a fresh instance each time.
///
/// <para>
/// Deliberately does not hand out a shared <c>IConnectionMultiplexer</c>: Microsoft's
/// <c>RedisCache.Dispose()</c> closes and disposes whatever multiplexer it was given, whether or
/// not it created it. Every caller here builds its own connection off <see cref="ConnectionString"/>
/// instead, so one test's (or one FusionCache instance's GC finalizer's) cleanup cannot pull the
/// connection out from under a test that is still running.
/// </para>
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private RedisContainer? _container;

    public string ConnectionString =>
        _container?.GetConnectionString() ?? throw new InvalidOperationException("The fixture has not started yet.");

    public async ValueTask InitializeAsync()
    {
        _container = new RedisBuilder("redis:7-alpine").Build();
        await _container.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>A prefix unique enough that two tests sharing the one container never see each
    /// other's keys.</summary>
    public static string NewKeyPrefix() => $"test:{Guid.NewGuid():N}:";
}

[CollectionDefinition(nameof(RedisCollection))]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>;
