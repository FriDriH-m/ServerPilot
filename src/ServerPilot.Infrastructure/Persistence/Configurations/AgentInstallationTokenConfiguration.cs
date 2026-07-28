using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerPilot.Domain.InstallationTokens;
using ServerPilot.Domain.Users;

namespace ServerPilot.Infrastructure.Persistence.Configurations;

internal sealed class AgentInstallationTokenConfiguration
    : IEntityTypeConfiguration<AgentInstallationToken>
{
    internal const string TokenHashUniqueIndexName =
        "ux_agent_installation_tokens_token_hash";
    internal const string UserCreatedAtIndexName =
        "ix_agent_installation_tokens_user_id_created_at";
    internal const string ValidLifetimeConstraintName =
        "ck_agent_installation_tokens_valid_lifetime";
    internal const string SingleTerminalStateConstraintName =
        "ck_agent_installation_tokens_single_terminal_state";

    public void Configure(EntityTypeBuilder<AgentInstallationToken> builder)
    {
        builder.ToTable(
            "agent_installation_tokens",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    ValidLifetimeConstraintName,
                    "expires_at > created_at");
                tableBuilder.HasCheckConstraint(
                    SingleTerminalStateConstraintName,
                    "used_at IS NULL OR revoked_at IS NULL");
            });
        builder.HasKey(token => token.Id).HasName("pk_agent_installation_tokens");

        builder.Property(token => token.Id).HasColumnName("id");
        builder.Property(token => token.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(token => token.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(AgentInstallationToken.TokenHashLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(token => token.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(token => token.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(token => token.UsedAt)
            .HasColumnName("used_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(token => token.RevokedAt)
            .HasColumnName("revoked_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName(TokenHashUniqueIndexName);
        builder.HasIndex(token => new { token.UserId, token.CreatedAt })
            .HasDatabaseName(UserCreatedAtIndexName);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_agent_installation_tokens_users_user_id");
    }
}
