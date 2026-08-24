using FeatureFlags.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace FeatureFlags.Infrastructure.Tests;

/// <summary>
/// That <c>AddFlagTargeting</c> can run against a database that already has flags in it.
///
/// <para>
/// Every other test here migrates an empty schema, which is also all the scaffolder ever sees — and
/// an empty schema accepts a bare <c>ADD COLUMN ... NOT NULL</c> that Postgres refuses the moment
/// one row exists. So this is the only test in the suite that would have caught the generated
/// migration being wrong, and it is why it builds its own database instead of using the fixture's
/// already-migrated one.
/// </para>
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class FlagTargetingMigrationTests(PostgresFixture postgres)
{
    /// <summary>The migration immediately before segments arrived — where a database that predates
    /// this work stands.</summary>
    private const string BeforeSegments = "20260818223309_AddFlagEventCausedBy";

    [Fact]
    public async Task Migrating_ADatabaseThatAlreadyHasFlags_ShouldLeaveThemTargetingNobody()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var databaseName = $"targeting_probe_{Guid.NewGuid():N}";

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
        await migrator.MigrateAsync(BeforeSegments, cancellationToken);

        // A flag as it stood before any of this — the row AddFlagTargeting has to widen.
        var flagId = Guid.CreateVersion7();
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO feature_flags ("Id", "Key", "Name", "Description", "CreatedAt", "UpdatedAt")
            VALUES ({0}, 'pre-existing', 'Pre-existing', '', now(), now());

            INSERT INTO feature_flag_states ("FlagId", "Environment", "IsEnabled", "UpdatedAt")
            VALUES ({0}, 'dev', true, now()),
                   ({0}, 'stg', false, now()),
                   ({0}, 'prod', false, now());
            """,
            [flagId],
            cancellationToken);

        await migrator.MigrateAsync(cancellationToken: cancellationToken);

        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            SELECT count(*)
            FROM feature_flag_states
            WHERE "FlagId" = @flagId AND "TargetedSegments" = '{}';
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "flagId";
        parameter.Value = flagId;
        command.Parameters.Add(parameter);

        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        var untargeted = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));

        // Empty, not null and not absent — and no backfill wrote it. The column default and
        // FlagState's own default agree, so replaying an existing stream already produces exactly
        // this, which is the question worth asking of any new field on an event-sourced table.
        Assert.Equal(3, untargeted);
    }
}
