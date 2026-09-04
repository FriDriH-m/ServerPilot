using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerPilot.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddLocalBackups : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_server_commands_valid_type_and_status",
            table: "server_commands");

        migrationBuilder.CreateTable(
            name: "backups",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                size_bytes = table.Column<long>(type: "bigint", nullable: true),
                checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_backups", x => x.id);
                table.CheckConstraint("ck_backups_valid_artifact", "status BETWEEN 1 AND 4 AND ((status = 3 AND size_bytes > 0 AND size_bytes <= 107374182400 AND checksum ~ '^[0-9A-F]{64}$' AND size_bytes IS NOT NULL AND checksum IS NOT NULL) OR (status <> 3 AND size_bytes IS NULL AND checksum IS NULL))");
                table.ForeignKey(
                    name: "fk_backups_server_commands_id",
                    column: x => x.id,
                    principalTable: "server_commands",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_server_commands_valid_type_and_status",
            table: "server_commands",
            sql: "type BETWEEN 1 AND 3 AND status BETWEEN 1 AND 7 AND attempt_count >= 0");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "backups");

        migrationBuilder.DropCheckConstraint(
            name: "ck_server_commands_valid_type_and_status",
            table: "server_commands");

        migrationBuilder.AddCheckConstraint(
            name: "ck_server_commands_valid_type_and_status",
            table: "server_commands",
            sql: "type BETWEEN 1 AND 2 AND status BETWEEN 1 AND 7 AND attempt_count >= 0");
    }
}
