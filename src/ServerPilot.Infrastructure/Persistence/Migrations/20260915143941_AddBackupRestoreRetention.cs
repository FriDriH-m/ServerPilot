using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerPilot.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddBackupRestoreRetention : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_server_commands_valid_type_and_status",
            table: "server_commands");

        migrationBuilder.DropCheckConstraint(
            name: "ck_backups_valid_artifact",
            table: "backups");

        migrationBuilder.AddColumn<string>(
            name: "backup_targets",
            table: "server_commands",
            type: "jsonb",
            nullable: false,
            defaultValueSql: "'[]'::jsonb");

        migrationBuilder.AddCheckConstraint(
            name: "ck_server_commands_backup_targets",
            table: "server_commands",
            sql: "jsonb_typeof(backup_targets) = 'array' AND ((type = 4 AND jsonb_array_length(backup_targets) = 1) OR (type = 5 AND jsonb_array_length(backup_targets) BETWEEN 1 AND 1000) OR (type IN (1, 2, 3) AND jsonb_array_length(backup_targets) = 0))");

        migrationBuilder.AddCheckConstraint(
            name: "ck_server_commands_valid_type_and_status",
            table: "server_commands",
            sql: "type BETWEEN 1 AND 5 AND status BETWEEN 1 AND 7 AND attempt_count >= 0");

        migrationBuilder.AddCheckConstraint(
            name: "ck_backups_valid_artifact",
            table: "backups",
            sql: "status BETWEEN 1 AND 6 AND ((status IN (3, 5, 6) AND size_bytes > 0 AND size_bytes <= 107374182400 AND checksum ~ '^[0-9A-F]{64}$' AND size_bytes IS NOT NULL AND checksum IS NOT NULL) OR (status IN (1, 2, 4) AND size_bytes IS NULL AND checksum IS NULL))");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_server_commands_backup_targets",
            table: "server_commands");

        migrationBuilder.DropCheckConstraint(
            name: "ck_server_commands_valid_type_and_status",
            table: "server_commands");

        migrationBuilder.DropCheckConstraint(
            name: "ck_backups_valid_artifact",
            table: "backups");

        migrationBuilder.DropColumn(
            name: "backup_targets",
            table: "server_commands");

        migrationBuilder.AddCheckConstraint(
            name: "ck_server_commands_valid_type_and_status",
            table: "server_commands",
            sql: "type BETWEEN 1 AND 3 AND status BETWEEN 1 AND 7 AND attempt_count >= 0");

        migrationBuilder.AddCheckConstraint(
            name: "ck_backups_valid_artifact",
            table: "backups",
            sql: "status BETWEEN 1 AND 4 AND ((status = 3 AND size_bytes > 0 AND size_bytes <= 107374182400 AND checksum ~ '^[0-9A-F]{64}$' AND size_bytes IS NOT NULL AND checksum IS NOT NULL) OR (status <> 3 AND size_bytes IS NULL AND checksum IS NULL))");
    }
}
