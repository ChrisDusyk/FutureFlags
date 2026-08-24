using FeatureFlags.Domain.Flags;
using FeatureFlags.Domain.SdkKeys;
using FeatureFlags.Domain.Segments;
using FeatureFlags.Domain.Users;
using FeatureFlags.Infrastructure.Persistence;
using FeatureFlags.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FeatureFlags.Infrastructure;

public static class DependencyInjection
{
    public static TBuilder AddInfrastructure<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.AddNpgsqlDbContext<AppDbContext>("featureflagsdb");

        builder.Services.AddScoped<IFeatureFlagRepository, FeatureFlagRepository>();
        builder.Services.AddScoped<IFlagViewRepository, FlagViewRepository>();
        builder.Services.AddScoped<ISegmentRepository, SegmentRepository>();
        builder.Services.AddScoped<ISegmentViewRepository, SegmentViewRepository>();
        builder.Services.AddScoped<ISdkKeyRepository, SdkKeyRepository>();
        builder.Services.AddScoped<IUserRepository, UserRepository>();

        return builder;
    }

    /// <summary>
    /// A fixed key for the Postgres advisory lock taken around migrations. The value is arbitrary
    /// and only has to be stable and unlikely to collide with another application's lock on the
    /// same database — advisory locks share one namespace per database.
    /// </summary>
    private const long MigrationLockId = 0x_FEA7_F1A6_0001;

    /// <summary>
    /// Brings the database up to the latest migration.
    ///
    /// Called during startup when <c>FEATUREFLAGS_APPLY_MIGRATIONS</c> asks for it — the AppHost's
    /// Postgres container relies on that in development, and the compose bundle turns it on because
    /// it runs a single replica. Anything running more than one instance should migrate as a
    /// deliberate step instead, which is what the Helm chart's job does.
    ///
    /// The advisory lock is what makes the in-process path survive being wrong about that: two
    /// servers starting together serialise here instead of racing each other through the same
    /// migration. It is held on one explicitly-opened connection so that it covers the whole
    /// migration and is released with it — a session-scoped lock would otherwise be dropped the
    /// moment EF returned the connection to the pool.
    /// </summary>
    public static async Task ApplyMigrationsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var database = dbContext.Database;

        await database.OpenConnectionAsync(cancellationToken);

        try
        {
            await database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_lock({0})", [MigrationLockId], cancellationToken);

            await database.MigrateAsync(cancellationToken);
        }
        finally
        {
            // Deliberately not cancellable: an unreleased advisory lock would block the next
            // startup until this connection died on its own.
            await database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock({0})", [MigrationLockId], CancellationToken.None);
            await database.CloseConnectionAsync();
        }
    }
}
