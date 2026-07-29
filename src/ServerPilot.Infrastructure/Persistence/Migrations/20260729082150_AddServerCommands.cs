using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerPilot.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddServerCommands : Migration
{
    private static readonly string[] AgentIdIdColumns = ["agent_id", "id"];
    private static readonly string[] AgentServerInstanceColumns =
        ["agent_id", "server_instance_id"];
    private static readonly string[] AgentStatusCreatedAtIdColumns =
        ["agent_id", "status", "created_at", "id"];
    private static readonly string[] ServerInstanceCreatedAtIdColumns =
        ["server_instance_id", "created_at", "id"];
    private static readonly bool[] ServerInstanceCreatedAtIdDescending = [false, true, true];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddUniqueConstraint(
            name: "ak_server_instances_agent_id_id",
            table: "server_instances",
            columns: AgentIdIdColumns);

        migrationBuilder.CreateTable(
            name: "server_commands",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                server_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                error_message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                attempt_count = table.Column<int>(type: "integer", nullable: false),
                correlation_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_server_commands", x => x.id);
                table.CheckConstraint("ck_server_commands_valid_failure_details", "(status = 5 AND error_code IS NOT NULL AND error_code = btrim(error_code) AND error_code <> '' AND error_message IS NOT NULL AND error_message = btrim(error_message) AND error_message <> '') OR (status <> 5 AND error_code IS NULL AND error_message IS NULL)");
                table.CheckConstraint("ck_server_commands_valid_state", "(status = 1 AND attempt_count = 0 AND claimed_at IS NULL AND started_at IS NULL AND completed_at IS NULL) OR (status = 2 AND attempt_count > 0 AND claimed_at IS NOT NULL AND started_at IS NULL AND completed_at IS NULL) OR (status = 3 AND attempt_count > 0 AND claimed_at IS NOT NULL AND started_at IS NOT NULL AND completed_at IS NULL) OR (status IN (4, 5) AND attempt_count > 0 AND claimed_at IS NOT NULL AND started_at IS NOT NULL AND completed_at IS NOT NULL) OR (status = 6 AND attempt_count = 0 AND claimed_at IS NULL AND started_at IS NULL AND completed_at IS NOT NULL) OR (status = 7 AND completed_at IS NOT NULL AND ((attempt_count = 0 AND claimed_at IS NULL AND started_at IS NULL) OR (attempt_count > 0 AND claimed_at IS NOT NULL)))");
                table.CheckConstraint("ck_server_commands_valid_timestamps", "(claimed_at IS NULL OR claimed_at >= created_at) AND (started_at IS NULL OR (claimed_at IS NOT NULL AND started_at >= claimed_at)) AND (completed_at IS NULL OR completed_at >= COALESCE(started_at, claimed_at, created_at))");
                table.CheckConstraint("ck_server_commands_valid_type_and_status", "type BETWEEN 1 AND 2 AND status BETWEEN 1 AND 7 AND attempt_count >= 0");
                table.ForeignKey(
                    name: "fk_server_commands_server_instances_agent_id_server_instance_id",
                    columns: x => new { x.agent_id, x.server_instance_id },
                    principalTable: "server_instances",
                    principalColumns: AgentIdIdColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_server_commands_agent_id_server_instance_id",
            table: "server_commands",
            columns: AgentServerInstanceColumns);

        migrationBuilder.CreateIndex(
            name: "ix_server_commands_agent_id_status_created_at_id",
            table: "server_commands",
            columns: AgentStatusCreatedAtIdColumns);

        migrationBuilder.CreateIndex(
            name: "ix_server_commands_server_instance_id_created_at_id",
            table: "server_commands",
            columns: ServerInstanceCreatedAtIdColumns,
            descending: ServerInstanceCreatedAtIdDescending);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "server_commands");

        migrationBuilder.DropUniqueConstraint(
            name: "ak_server_instances_agent_id_id",
            table: "server_instances");
    }
}
