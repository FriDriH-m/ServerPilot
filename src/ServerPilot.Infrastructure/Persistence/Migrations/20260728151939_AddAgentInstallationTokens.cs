using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerPilot.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddAgentInstallationTokens : Migration
{
    private static readonly string[] UserCreatedAtIndexColumns =
        ["user_id", "created_at"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "agent_installation_tokens",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_agent_installation_tokens", x => x.id);
                table.CheckConstraint("ck_agent_installation_tokens_single_terminal_state", "used_at IS NULL OR revoked_at IS NULL");
                table.CheckConstraint("ck_agent_installation_tokens_valid_lifetime", "expires_at > created_at");
                table.ForeignKey(
                    name: "fk_agent_installation_tokens_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_agent_installation_tokens_user_id_created_at",
            table: "agent_installation_tokens",
            columns: UserCreatedAtIndexColumns);

        migrationBuilder.CreateIndex(
            name: "ux_agent_installation_tokens_token_hash",
            table: "agent_installation_tokens",
            column: "token_hash",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "agent_installation_tokens");
    }
}
