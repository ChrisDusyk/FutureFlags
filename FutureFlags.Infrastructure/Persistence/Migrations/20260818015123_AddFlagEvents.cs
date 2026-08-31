using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FutureFlags.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFlagEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "flag_events",
                columns: table => new
                {
                    FlagId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNumber = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flag_events", x => new { x.FlagId, x.SequenceNumber });
                });

            // Every existing flag predates event sourcing, so it gets a synthesized history rather
            // than a real one: one FlagCreated event carrying its current name/description, followed
            // by one FlagStateChanged per environment carrying its current on/off value. This is
            // lossy on real toggle history — a flag toggled five times collapses to the one event
            // carrying what it is now — which is accepted because no real history exists to preserve.
            migrationBuilder.Sql(
                """
                INSERT INTO flag_events ("FlagId", "SequenceNumber", "EventType", "Payload", "OccurredAt")
                SELECT f."Id", 1, 'FlagCreated',
                       jsonb_build_object('key', f."Key", 'name', f."Name", 'description', f."Description"),
                       f."CreatedAt"
                FROM feature_flags AS f;
                """);

            // Numbered 2/3/4 in dev/stg/prod order — the same order FeatureFlag.Create raises them
            // in today, so a freshly-rehydrated flag folds to exactly the state it already has.
            // Every state's UpdatedAt is >= its flag's CreatedAt under the current domain rules (a
            // state can't be touched before its flag exists), so this sequence-number order and the
            // OccurredAt order agree for every backfilled flag.
            migrationBuilder.Sql(
                """
                INSERT INTO flag_events ("FlagId", "SequenceNumber", "EventType", "Payload", "OccurredAt")
                SELECT s."FlagId",
                       1 + ROW_NUMBER() OVER (
                           PARTITION BY s."FlagId"
                           ORDER BY CASE s."Environment" WHEN 'dev' THEN 1 WHEN 'stg' THEN 2 WHEN 'prod' THEN 3 END),
                       'FlagStateChanged',
                       jsonb_build_object('environment', s."Environment", 'isEnabled', s."IsEnabled"),
                       s."UpdatedAt"
                FROM feature_flag_states AS s;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "flag_events");
        }
    }
}
