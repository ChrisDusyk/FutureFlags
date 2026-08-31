using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FutureFlags.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSdkKeyKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfilled as "secret", not as the empty string EF defaults to. Every key that
            // exists when this runs was issued when secret was the only kind there was, so that is
            // what they are — and an empty string is not a kind SdkKeyKind.FromPersisted will
            // rehydrate, so the alternative is every existing key throwing on its next use.
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "sdk_keys",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "secret");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "sdk_keys");
        }
    }
}
