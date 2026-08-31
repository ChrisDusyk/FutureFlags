using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FutureFlags.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersMirror : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email",
                unique: true);

            // Keeping the mirror in step is the database's job, not the application's. A trigger
            // makes the copy part of the same transaction as the write Better Auth performs, so
            // the two schemas cannot drift the way an after-the-fact hook would allow.
            //
            // This requires auth."user" to already exist, which is why the AppHost holds the
            // server back until the auth service is healthy — that service creates the schema
            // and runs Better Auth's own migrations at startup.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public.mirror_auth_user()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF (TG_OP = 'DELETE') THEN
                        DELETE FROM public.users WHERE "Id" = OLD.id::uuid;
                        RETURN OLD;
                    END IF;

                    INSERT INTO public.users ("Id", "Email", "Name", "Role", "CreatedAt", "UpdatedAt")
                    VALUES (
                        NEW.id::uuid,
                        NEW.email,
                        NEW.name,
                        -- Better Auth permits a comma-separated list of roles; this application
                        -- recognizes exactly two, so anything holding 'admin' is an admin and
                        -- everything else — including NULL — is an ordinary user.
                        CASE
                            WHEN COALESCE(NEW.role, '') ~ '(^|,)\s*admin\s*($|,)' THEN 'admin'
                            ELSE 'user'
                        END,
                        NEW."createdAt",
                        NEW."updatedAt")
                    ON CONFLICT ("Id") DO UPDATE SET
                        "Email" = EXCLUDED."Email",
                        "Name" = EXCLUDED."Name",
                        "Role" = EXCLUDED."Role",
                        "UpdatedAt" = EXCLUDED."UpdatedAt";

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER mirror_auth_user
                AFTER INSERT OR UPDATE OR DELETE ON auth."user"
                FOR EACH ROW EXECUTE FUNCTION public.mirror_auth_user();
                """);

            // Anyone who signed up before this migration ran still needs a row.
            migrationBuilder.Sql(
                """
                INSERT INTO public.users ("Id", "Email", "Name", "Role", "CreatedAt", "UpdatedAt")
                SELECT
                    source.id::uuid,
                    source.email,
                    source.name,
                    CASE
                        WHEN COALESCE(source.role, '') ~ '(^|,)\s*admin\s*($|,)' THEN 'admin'
                        ELSE 'user'
                    END,
                    source."createdAt",
                    source."updatedAt"
                FROM auth."user" AS source
                ON CONFLICT ("Id") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS mirror_auth_user ON auth."user";""");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.mirror_auth_user();");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
