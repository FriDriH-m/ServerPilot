using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerPilot.Domain.Agents;
using ServerPilot.Domain.Users;

namespace ServerPilot.Infrastructure.Persistence.Configurations;

internal sealed class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    internal const string CredentialHashUniqueIndexName = "ux_agents_credential_hash";
    internal const string UserRegisteredAtIdIndexName =
        "ix_agents_user_id_registered_at_id";
    internal const string ValidCredentialHashConstraintName =
        "ck_agents_valid_credential_hash";
    internal const string ValidCredentialRevokedAtConstraintName =
        "ck_agents_valid_credential_revoked_at";
    internal const string ValidLastSeenAtConstraintName =
        "ck_agents_valid_last_seen_at";
    internal const string TrimmedMetadataConstraintName = "ck_agents_trimmed_metadata";

    public void Configure(EntityTypeBuilder<Agent> builder)
    {
        builder.ToTable(
            "agents",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    ValidCredentialHashConstraintName,
                    "credential_hash ~ '^[0-9a-f]{64}$'");
                tableBuilder.HasCheckConstraint(
                    ValidCredentialRevokedAtConstraintName,
                    "credential_revoked_at IS NULL OR credential_revoked_at >= registered_at");
                tableBuilder.HasCheckConstraint(
                    ValidLastSeenAtConstraintName,
                    "last_seen_at IS NULL OR last_seen_at >= registered_at");
                tableBuilder.HasCheckConstraint(
                    TrimmedMetadataConstraintName,
                    "name = btrim(name) AND name <> '' AND " +
                    "machine_name = btrim(machine_name) AND machine_name <> '' AND " +
                    "operating_system = btrim(operating_system) AND operating_system <> '' AND " +
                    "agent_version = btrim(agent_version) AND agent_version <> ''");
            });
        builder.HasKey(agent => agent.Id).HasName("pk_agents");

        builder.Property(agent => agent.Id).HasColumnName("id");
        builder.Property(agent => agent.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(agent => agent.Name)
            .HasColumnName("name")
            .HasMaxLength(Agent.MaximumNameLength)
            .IsRequired();
        builder.Property(agent => agent.MachineName)
            .HasColumnName("machine_name")
            .HasMaxLength(Agent.MaximumMachineNameLength)
            .IsRequired();
        builder.Property(agent => agent.OperatingSystem)
            .HasColumnName("operating_system")
            .HasMaxLength(Agent.MaximumOperatingSystemLength)
            .IsRequired();
        builder.Property(agent => agent.Version)
            .HasColumnName("agent_version")
            .HasMaxLength(Agent.MaximumVersionLength)
            .IsRequired();
        builder.Property(agent => agent.CredentialHash)
            .HasColumnName("credential_hash")
            .HasMaxLength(Agent.CredentialHashLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(agent => agent.RegisteredAt)
            .HasColumnName("registered_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(agent => agent.LastSeenAt)
            .HasColumnName("last_seen_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(agent => agent.CredentialRevokedAt)
            .HasColumnName("credential_revoked_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(agent => agent.CredentialHash)
            .IsUnique()
            .HasDatabaseName(CredentialHashUniqueIndexName);
        builder.HasIndex(agent => new { agent.UserId, agent.RegisteredAt, agent.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName(UserRegisteredAtIdIndexName);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(agent => agent.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_agents_users_user_id");
    }
}
