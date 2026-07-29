using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerPilot.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddActiveServerCommandConstraint : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ux_server_commands_active_server_instance_id",
            table: "server_commands",
            column: "server_instance_id",
            unique: true,
            filter: "status IN (1, 2, 3)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_server_commands_active_server_instance_id",
            table: "server_commands");
    }
}
