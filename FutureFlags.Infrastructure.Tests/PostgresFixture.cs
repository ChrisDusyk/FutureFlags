using FutureFlags.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace FutureFlags.Infrastructure.Tests;

/// <summary>
/// One Postgres container for the whole collection, fully migrated once at startup — starting a
/// container and migrating it are both too slow to redo per test.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString =>
        _container?.GetConnectionString() ?? throw new InvalidOperationException("The fixture has not started yet.");

    public async ValueTask InitializeAsync()
    {
        // Matches the Postgres major version the deploy artifacts run
        // (deploy/compose/docker-compose.yml, deploy/helm/futureflags/values.yaml) — a newer
        // major here could pass while hiding an incompatibility with what a self-hoster actually runs.
        _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await _container.StartAsync();

        await using var dbContext = NewDbContext();

        // The AddUsersMirror migration puts a trigger on auth."user" — normally created by the
        // separate auth service before the server ever migrates (see AppHost's WaitFor(auth)).
        // There is no auth service here, so a bare stand-in table is enough for the migration
        // itself to succeed; nothing in these tests writes to it.
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE SCHEMA IF NOT EXISTS auth;
            CREATE TABLE IF NOT EXISTS auth."user" (
                id text PRIMARY KEY,
                email text NOT NULL,
                name text NOT NULL,
                role text,
                "createdAt" timestamptz NOT NULL,
                "updatedAt" timestamptz NOT NULL
            );
            """);

        await dbContext.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    public AppDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
