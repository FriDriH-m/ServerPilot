using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerPilot.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddAgentHeartbeatAndQueries : Migration
{
    private static readonly string[] UserRegisteredAtIdColumns =
        ["user_id", "registered_at", "id"];
    private static readonly bool[] UserRegisteredAtIdDescending =
        [false, true, true];
    private static readonly string[] UserRegisteredAtColumns =
        ["user_id", "registered_at"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_agents_user_id_registered_at",
            table: "agents");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "last_seen_at",
            table: "agents",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_agents_user_id_registered_at_id",
            table: "agents",
            columns: UserRegisteredAtIdColumns,
            descending: UserRegisteredAtIdDescending);

        migrationBuilder.AddCheckConstraint(
            name: "ck_agents_valid_last_seen_at",
            table: "agents",
            sql: "last_seen_at IS NULL OR last_seen_at >= registered_at");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_agents_user_id_registered_at_id",
            table: "agents");

        migrationBuilder.DropCheckConstraint(
            name: "ck_agents_valid_last_seen_at",
            table: "agents");

        migrationBuilder.DropColumn(
            name: "last_seen_at",
            table: "agents");

        migrationBuilder.CreateIndex(
            name: "ix_agents_user_id_registered_at",
            table: "agents",
            columns: UserRegisteredAtColumns);
    }
}
