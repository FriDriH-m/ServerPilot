using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerPilot.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class HardenAgentCommandDelivery : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $migration$
            BEGIN
                IF EXISTS
                (
                    SELECT 1
                    FROM server_commands
                    WHERE status IN (2, 3)
                    GROUP BY agent_id
                    HAVING COUNT(*) > 1
                ) THEN
                    RAISE EXCEPTION
                        'Cannot add ux_server_commands_active_agent_id: an Agent has more than one Claimed or Running command.';
                END IF;
            END
            $migration$;
            """);

        migrationBuilder.CreateIndex(
            name: "ux_server_commands_active_agent_id",
            table: "server_commands",
            column: "agent_id",
            unique: true,
            filter: "status IN (2, 3)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_server_commands_active_agent_id",
            table: "server_commands");
    }
}
