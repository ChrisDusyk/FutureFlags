using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FutureFlags.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFlagEventCausedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CausedBy",
                table: "flag_events",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CausedBy",
                table: "flag_events");
        }
    }
}
