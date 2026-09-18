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
    /// predates this. The defaults are kept rather than dropped: during a rolling deployment a
    /// pre-variants server instance can still be writing rows through this migrated schema, and its
    /// INSERT omits these four columns entirely, which a dropped default turns into a NOT NULL
    /// violation instead of a boolean-shaped row. <c>FlagRowConfiguration</c> declares the same
    /// defaults on the model, so there is no drift for the next scaffold to "fix".
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
