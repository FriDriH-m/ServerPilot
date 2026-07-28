using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerPilot.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddAgentRegistrationAndAuthentication : Migration
{
    private static readonly string[] UserRegisteredAtColumns =
        ["user_id", "registered_at"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "agents",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                machine_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                operating_system = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                agent_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                credential_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                credential_revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_agents", x => x.id);
                table.CheckConstraint("ck_agents_trimmed_metadata", "name = btrim(name) AND name <> '' AND machine_name = btrim(machine_name) AND machine_name <> '' AND operating_system = btrim(operating_system) AND operating_system <> '' AND agent_version = btrim(agent_version) AND agent_version <> ''");
                table.CheckConstraint("ck_agents_valid_credential_hash", "credential_hash ~ '^[0-9a-f]{64}$'");
                table.CheckConstraint("ck_agents_valid_credential_revoked_at", "credential_revoked_at IS NULL OR credential_revoked_at >= registered_at");
                table.ForeignKey(
                    name: "fk_agents_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_agents_user_id_registered_at",
            table: "agents",
            columns: UserRegisteredAtColumns);

        migrationBuilder.CreateIndex(
            name: "ux_agents_credential_hash",
            table: "agents",
            column: "credential_hash",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "agents");
    }
}
