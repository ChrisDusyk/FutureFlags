using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FutureFlags.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Gives a flag a value type and a set of named variants, and gives each environment's state
    /// the variant names it serves.
    ///
    /// <para>
    /// Every column is added with a default so existing rows are backfilled in the same statement
    /// rather than left holding a value the model cannot read — the scaffolded empty strings would
    /// have made <c>FlagValueType.FromPersisted</c> throw on the first read of any flag that
    /// predates this. The defaults are then dropped, because the model does not declare any and a
    /// column default it does not know about is drift the next scaffold would try to undo.
    /// </para>
    /// <para>
    /// This touches only <c>public</c> tables and has no dependency on <c>auth."user"</c>, unlike
    /// <c>AddUsersMirror</c> — nothing here needs the auth service to have started.
    /// </para>
    /// </summary>
    public partial class AddFlagVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ValueType",
                table: "feature_flags",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "boolean");

            // The key order here is irrelevant: FlagVariants.FromPersisted re-sorts into the normal
            // form on read, so this only has to be the right set of names and values.
            migrationBuilder.AddColumn<string>(
                name: "Variants",
                table: "feature_flags",
                type: "jsonb",
                nullable: false,
                defaultValue: "{\"off\":false,\"on\":true}");

            migrationBuilder.AddColumn<string>(
                name: "OnVariant",
                table: "feature_flag_states",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "on");

            migrationBuilder.AddColumn<string>(
                name: "OffVariant",
                table: "feature_flag_states",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "off");

            migrationBuilder.Sql("""
                ALTER TABLE feature_flags ALTER COLUMN "ValueType" DROP DEFAULT;
                ALTER TABLE feature_flags ALTER COLUMN "Variants" DROP DEFAULT;
                ALTER TABLE feature_flag_states ALTER COLUMN "OnVariant" DROP DEFAULT;
                ALTER TABLE feature_flag_states ALTER COLUMN "OffVariant" DROP DEFAULT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ValueType",
                table: "feature_flags");

            migrationBuilder.DropColumn(
                name: "Variants",
                table: "feature_flags");

            migrationBuilder.DropColumn(
                name: "OnVariant",
                table: "feature_flag_states");

            migrationBuilder.DropColumn(
                name: "OffVariant",
                table: "feature_flag_states");
        }
    }
}
