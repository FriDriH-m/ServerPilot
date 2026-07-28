using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerPilot.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddServerInstances : Migration
{
    private static readonly string[] AgentCreatedAtIdColumns =
        ["agent_id", "created_at", "id"];
    private static readonly bool[] AgentCreatedAtIdDescending =
        [false, true, true];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "server_instances",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                executable_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                arguments = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                working_directory = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                process_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                last_process_id = table.Column<int>(type: "integer", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_server_instances", x => x.id);
                table.CheckConstraint("ck_server_instances_trimmed_configuration", "name = btrim(name) AND name <> '' AND executable_path = btrim(executable_path) AND executable_path <> '' AND arguments = btrim(arguments) AND working_directory = btrim(working_directory) AND working_directory <> '' AND process_name = btrim(process_name) AND process_name <> '' AND position('/' in process_name) = 0 AND position(':' in process_name) = 0");
                table.CheckConstraint("ck_server_instances_valid_state", "status BETWEEN 1 AND 7 AND (last_process_id IS NULL OR last_process_id > 0)");
                table.CheckConstraint("ck_server_instances_valid_timestamps", "updated_at >= created_at");
                table.ForeignKey(
                    name: "fk_server_instances_agents_agent_id",
                    column: x => x.agent_id,
                    principalTable: "agents",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_server_instances_agent_id_created_at_id",
            table: "server_instances",
            columns: AgentCreatedAtIdColumns,
            descending: AgentCreatedAtIdDescending);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "server_instances");
    }
}
