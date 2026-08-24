using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeatureFlags.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFlagTargeting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The default is what lets this run against a database that already has flags in it.
            // Scaffolding produced a bare NOT NULL, which Postgres refuses outright the moment one
            // row exists — and the scaffold could not have known, because it only ever sees an
            // empty schema. On Postgres 11+ this stays a metadata-only change, so the migrate job
            // takes no table lock and pods still serving through the window never notice.
            migrationBuilder.AddColumn<List<string>>(
                name: "TargetedSegments",
                table: "feature_flag_states",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            // Dropped again immediately: the default existed only to fill the rows that were
            // already there, and leaving it would mean the database carries a default the model
            // knows nothing about — which the next scaffolded migration would notice and "fix".
            migrationBuilder.Sql(
                """ALTER TABLE feature_flag_states ALTER COLUMN "TargetedSegments" DROP DEFAULT;""");

            migrationBuilder.CreateIndex(
                name: "IX_feature_flag_states_TargetedSegments",
                table: "feature_flag_states",
                column: "TargetedSegments")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_feature_flag_states_TargetedSegments",
                table: "feature_flag_states");

            migrationBuilder.DropColumn(
                name: "TargetedSegments",
                table: "feature_flag_states");
        }
    }
}
