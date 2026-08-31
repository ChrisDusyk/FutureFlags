using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FutureFlags.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Moves a flag's on/off state off the flag and into one row per environment.
    /// <para>
    /// Hand-edited after scaffolding: the generated version dropped <c>IsEnabled</c> before the new
    /// table existed, which would have thrown every flag's state away. The order here is create,
    /// backfill, then drop, so the column is only removed once its contents have somewhere to live.
    /// </para>
    /// </summary>
    public partial class AddFlagStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "feature_flag_states",
                columns: table => new
                {
                    Environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FlagId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feature_flag_states", x => new { x.FlagId, x.Environment });
                    table.ForeignKey(
                        name: "FK_feature_flag_states_feature_flags_FlagId",
                        column: x => x.FlagId,
                        principalTable: "feature_flags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Every existing flag gains a state in every environment, all carrying the value it had
            // before. That value genuinely was the answer everywhere — there was only one — so
            // copying it across is the faithful reading rather than a guess.
            migrationBuilder.Sql(
                """
                INSERT INTO feature_flag_states ("FlagId", "Environment", "IsEnabled", "UpdatedAt")
                SELECT f."Id", e."Environment", f."IsEnabled", f."UpdatedAt"
                FROM feature_flags AS f
                CROSS JOIN (VALUES ('dev'), ('stg'), ('prod')) AS e("Environment");
                """);

            migrationBuilder.DropColumn(
                name: "IsEnabled",
                table: "feature_flags");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled",
                table: "feature_flags",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Going back to a single value means picking one environment to be it. Production is the
            // only defensible choice: it is the state that was actually reaching people.
            migrationBuilder.Sql(
                """
                UPDATE feature_flags AS f
                SET "IsEnabled" = s."IsEnabled"
                FROM feature_flag_states AS s
                WHERE s."FlagId" = f."Id" AND s."Environment" = 'prod';
                """);

            migrationBuilder.DropTable(
                name: "feature_flag_states");
        }
    }
}
