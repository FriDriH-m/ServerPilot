using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerPilot.Domain.Agents;
using ServerPilot.Domain.ServerInstances;
using ServerInstanceDomainConfiguration =
    ServerPilot.Domain.ServerInstances.ServerInstanceConfiguration;

namespace ServerPilot.Infrastructure.Persistence.Configurations;

internal sealed class ServerInstanceConfiguration : IEntityTypeConfiguration<ServerInstance>
{
    internal const string AgentCreatedAtIdIndexName =
        "ix_server_instances_agent_id_created_at_id";
    internal const string ValidStateConstraintName =
        "ck_server_instances_valid_state";
    internal const string ValidTimestampsConstraintName =
        "ck_server_instances_valid_timestamps";
    internal const string TrimmedConfigurationConstraintName =
        "ck_server_instances_trimmed_configuration";

    public void Configure(EntityTypeBuilder<ServerInstance> builder)
    {
        builder.ToTable(
            "server_instances",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    ValidStateConstraintName,
                    "status BETWEEN 1 AND 7 AND " +
                    "(last_process_id IS NULL OR last_process_id > 0)");
                tableBuilder.HasCheckConstraint(
                    ValidTimestampsConstraintName,
                    "updated_at >= created_at");
                tableBuilder.HasCheckConstraint(
                    TrimmedConfigurationConstraintName,
                    "name = btrim(name) AND name <> '' AND " +
                    "executable_path = btrim(executable_path) AND executable_path <> '' AND " +
                    "arguments = btrim(arguments) AND " +
                    "working_directory = btrim(working_directory) AND working_directory <> '' AND " +
                    "process_name = btrim(process_name) AND process_name <> '' AND " +
                    "position('/' in process_name) = 0 AND position(':' in process_name) = 0");
            });
        builder.HasKey(serverInstance => serverInstance.Id)
            .HasName("pk_server_instances");

        builder.Property(serverInstance => serverInstance.Id).HasColumnName("id");
        builder.Property(serverInstance => serverInstance.AgentId)
            .HasColumnName("agent_id")
            .IsRequired();
        builder.Property(serverInstance => serverInstance.Name)
            .HasColumnName("name")
            .HasMaxLength(ServerInstanceDomainConfiguration.MaximumNameLength)
            .IsRequired();
        builder.Property(serverInstance => serverInstance.ExecutablePath)
            .HasColumnName("executable_path")
            .HasMaxLength(ServerInstanceDomainConfiguration.MaximumExecutablePathLength)
            .IsRequired();
        builder.Property(serverInstance => serverInstance.Arguments)
            .HasColumnName("arguments")
            .HasMaxLength(ServerInstanceDomainConfiguration.MaximumArgumentsLength)
            .IsRequired();
        builder.Property(serverInstance => serverInstance.WorkingDirectory)
            .HasColumnName("working_directory")
            .HasMaxLength(ServerInstanceDomainConfiguration.MaximumWorkingDirectoryLength)
            .IsRequired();
        builder.Property(serverInstance => serverInstance.ProcessName)
            .HasColumnName("process_name")
            .HasMaxLength(ServerInstanceDomainConfiguration.MaximumProcessNameLength)
            .IsRequired();
        builder.Property(serverInstance => serverInstance.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();
        builder.Property(serverInstance => serverInstance.LastProcessId)
            .HasColumnName("last_process_id");
        builder.Property(serverInstance => serverInstance.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(serverInstance => serverInstance.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(serverInstance => new
        {
            serverInstance.AgentId,
            serverInstance.CreatedAt,
            serverInstance.Id,
        })
            .IsDescending(false, true, true)
            .HasDatabaseName(AgentCreatedAtIdIndexName);

        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(serverInstance => serverInstance.AgentId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_server_instances_agents_agent_id");
    }
}
