using FutureFlags.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace FutureFlags.Infrastructure.Tests;

/// <summary>
/// That <c>AddFlagVariants</c> can run against a database that already has flags in it.
///
/// <para>
/// The same gap <see cref="FlagTargetingMigrationTests"/> exists to close, and a sharper one here:
/// the scaffolder generated four <c>ADD COLUMN ... NOT NULL DEFAULT ''</c> statements, which an
/// empty schema accepts happily. Every existing row would then have carried an empty value type and
/// empty JSON for its variants — and <c>FlagValueType.FromPersisted("")</c> throws, so the first
/// read of any flag predating this would have failed, on a migration that reported success.
/// </para>
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class FlagVariantsMigrationTests(PostgresFixture postgres)
{
    /// <summary>The migration immediately before variants arrived.</summary>
    private const string BeforeVariants = "20260821173321_AddFlagTargeting";

    [Fact]
    public async Task Migrating_ADatabaseThatAlreadyHasFlags_ShouldBackfillTheBooleanShape()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var databaseName = $"variants_probe_{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(postgres.ConnectionString))
        {
            await admin.OpenAsync(cancellationToken);
            await using var create = new NpgsqlCommand($"CREATE DATABASE {databaseName};", admin);
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        var builder = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = databaseName };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(builder.ConnectionString).Options;

        await using var dbContext = new AppDbContext(options);

        // AddUsersMirror puts a trigger on auth."user", which the auth service normally owns.
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
            """,
            cancellationToken);

        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeVariants, cancellationToken);

        var flagId = Guid.CreateVersion7();
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO feature_flags ("Id", "Key", "Name", "Description", "CreatedAt", "UpdatedAt")
            VALUES ({0}, 'pre-existing', 'Pre-existing', '', now(), now());

            INSERT INTO feature_flag_states ("FlagId", "Environment", "IsEnabled", "TargetedSegments", "UpdatedAt")
            VALUES ({0}, 'dev', true, '{}', now()),
                   ({0}, 'stg', false, '{}', now()),
                   ({0}, 'prod', false, '{}', now());
            """,
            [flagId],
            cancellationToken);

        await migrator.MigrateAsync(cancellationToken: cancellationToken);

        // Read it back through the model rather than through SQL: that is what actually proves the
        // backfilled values are ones FlagValueType and FlagVariants can rehydrate, which is the
        // failure the scaffolded defaults would have produced.
        var row = await dbContext.FlagRows
            .AsNoTracking()
            .Include(candidate => candidate.States)
            .SingleAsync(candidate => candidate.Id == flagId, cancellationToken);

        Assert.Equal(Domain.Flags.FlagValueType.Boolean, row.ValueType);
        Assert.Equal(Domain.Flags.FlagVariants.BooleanPair, row.Variants);
        Assert.All(row.States, state =>
        {
            Assert.Equal(Evaluation.FlagVariantNames.On, state.OnVariant);
            Assert.Equal(Evaluation.FlagVariantNames.Off, state.OffVariant);
        });

        // The defaults were dropped after the backfill, so the schema matches a model that declares
        // none — otherwise the next scaffolded migration would try to remove them.
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            SELECT count(*)
            FROM information_schema.columns
            WHERE table_name IN ('feature_flags', 'feature_flag_states')
              AND column_name IN ('ValueType', 'Variants', 'OnVariant', 'OffVariant')
              AND column_default IS NOT NULL;
            """;

        await dbContext.Database.OpenConnectionAsync(cancellationToken);

        Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)));
    }
}
