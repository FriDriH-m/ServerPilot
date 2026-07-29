using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerPilot.Domain.Commands;
using ServerPilot.Domain.ServerInstances;

namespace ServerPilot.Infrastructure.Persistence.Configurations;

internal sealed class ServerCommandConfiguration : IEntityTypeConfiguration<ServerCommand>
{
    internal const string AgentStatusCreatedAtIdIndexName =
        "ix_server_commands_agent_id_status_created_at_id";
    internal const string ServerInstanceCreatedAtIdIndexName =
        "ix_server_commands_server_instance_id_created_at_id";
    internal const string ValidTypeAndStatusConstraintName =
        "ck_server_commands_valid_type_and_status";
    internal const string ValidStateConstraintName = "ck_server_commands_valid_state";
    internal const string ValidTimestampsConstraintName =
        "ck_server_commands_valid_timestamps";
    internal const string ValidFailureDetailsConstraintName =
        "ck_server_commands_valid_failure_details";

    public void Configure(EntityTypeBuilder<ServerCommand> builder)
    {
        builder.ToTable(
            "server_commands",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    ValidTypeAndStatusConstraintName,
                    "type BETWEEN 1 AND 2 AND status BETWEEN 1 AND 7 AND attempt_count >= 0");
                tableBuilder.HasCheckConstraint(
                    ValidStateConstraintName,
                    "(status = 1 AND attempt_count = 0 AND claimed_at IS NULL AND " +
                    "started_at IS NULL AND completed_at IS NULL) OR " +
                    "(status = 2 AND attempt_count > 0 AND claimed_at IS NOT NULL AND " +
                    "started_at IS NULL AND completed_at IS NULL) OR " +
                    "(status = 3 AND attempt_count > 0 AND claimed_at IS NOT NULL AND " +
                    "started_at IS NOT NULL AND completed_at IS NULL) OR " +
                    "(status IN (4, 5) AND attempt_count > 0 AND claimed_at IS NOT NULL AND " +
                    "started_at IS NOT NULL AND completed_at IS NOT NULL) OR " +
                    "(status = 6 AND attempt_count = 0 AND claimed_at IS NULL AND " +
                    "started_at IS NULL AND completed_at IS NOT NULL) OR " +
                    "(status = 7 AND completed_at IS NOT NULL AND " +
                    "((attempt_count = 0 AND claimed_at IS NULL AND started_at IS NULL) OR " +
                    "(attempt_count > 0 AND claimed_at IS NOT NULL)))");
                tableBuilder.HasCheckConstraint(
                    ValidTimestampsConstraintName,
                    "(claimed_at IS NULL OR claimed_at >= created_at) AND " +
                    "(started_at IS NULL OR (claimed_at IS NOT NULL AND started_at >= claimed_at)) AND " +
                    "(completed_at IS NULL OR completed_at >= COALESCE(started_at, claimed_at, created_at))");
                tableBuilder.HasCheckConstraint(
                    ValidFailureDetailsConstraintName,
                    "(status = 5 AND error_code IS NOT NULL AND error_code = btrim(error_code) " +
                    "AND error_code <> '' AND error_message IS NOT NULL AND " +
                    "error_message = btrim(error_message) AND error_message <> '') OR " +
                    "(status <> 5 AND error_code IS NULL AND error_message IS NULL)");
            });
        builder.HasKey(command => command.Id).HasName("pk_server_commands");

        builder.Property(command => command.Id).HasColumnName("id");
        builder.Property(command => command.AgentId).HasColumnName("agent_id").IsRequired();
        builder.Property(command => command.ServerInstanceId)
            .HasColumnName("server_instance_id")
            .IsRequired();
        builder.Property(command => command.Type)
            .HasColumnName("type")
            .HasConversion<int>()
            .IsRequired();
        builder.Property(command => command.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();
        builder.Property(command => command.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(command => command.ClaimedAt)
            .HasColumnName("claimed_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(command => command.StartedAt)
            .HasColumnName("started_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(command => command.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(command => command.ErrorCode)
            .HasColumnName("error_code")
            .HasMaxLength(ServerCommand.MaximumErrorCodeLength);
        builder.Property(command => command.ErrorMessage)
            .HasColumnName("error_message")
            .HasMaxLength(ServerCommand.MaximumErrorMessageLength);
        builder.Property(command => command.AttemptCount)
            .HasColumnName("attempt_count")
            .IsRequired();
        builder.Property(command => command.CorrelationId)
            .HasColumnName("correlation_id")
            .IsRequired();

        builder.HasIndex(command => new
        {
            command.AgentId,
            command.Status,
            command.CreatedAt,
            command.Id,
        })
            .IsDescending(false, false, false, false)
            .HasDatabaseName(AgentStatusCreatedAtIdIndexName);
        builder.HasIndex(command => new
        {
            command.ServerInstanceId,
            command.CreatedAt,
            command.Id,
        })
            .IsDescending(false, true, true)
            .HasDatabaseName(ServerInstanceCreatedAtIdIndexName);

        builder.HasOne<ServerInstance>()
            .WithMany()
            .HasForeignKey(command => new { command.AgentId, command.ServerInstanceId })
            .HasPrincipalKey(serverInstance => new { serverInstance.AgentId, serverInstance.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_server_commands_server_instances_agent_id_server_instance_id");
    }
}
