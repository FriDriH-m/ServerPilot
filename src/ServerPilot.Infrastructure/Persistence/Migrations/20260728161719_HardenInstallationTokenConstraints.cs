using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerPilot.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class HardenInstallationTokenConstraints : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddCheckConstraint(
            name: "ck_agent_installation_tokens_valid_revoked_at",
            table: "agent_installation_tokens",
            sql: "revoked_at IS NULL OR revoked_at >= created_at");

        migrationBuilder.AddCheckConstraint(
            name: "ck_agent_installation_tokens_valid_token_hash",
            table: "agent_installation_tokens",
            sql: "token_hash ~ '^[0-9a-f]{64}$'");

        migrationBuilder.AddCheckConstraint(
            name: "ck_agent_installation_tokens_valid_used_at",
            table: "agent_installation_tokens",
            sql: "used_at IS NULL OR (used_at >= created_at AND used_at < expires_at)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_agent_installation_tokens_valid_revoked_at",
            table: "agent_installation_tokens");

        migrationBuilder.DropCheckConstraint(
            name: "ck_agent_installation_tokens_valid_token_hash",
            table: "agent_installation_tokens");

        migrationBuilder.DropCheckConstraint(
            name: "ck_agent_installation_tokens_valid_used_at",
            table: "agent_installation_tokens");
    }
}
