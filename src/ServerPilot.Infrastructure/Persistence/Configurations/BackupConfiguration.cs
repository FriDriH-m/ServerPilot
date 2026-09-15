using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerPilot.Domain.Backups;
using ServerPilot.Domain.Commands;

namespace ServerPilot.Infrastructure.Persistence.Configurations;

internal sealed class BackupConfiguration : IEntityTypeConfiguration<Backup>
{
    public void Configure(EntityTypeBuilder<Backup> builder)
    {
        builder.ToTable("backups", table => table.HasCheckConstraint("ck_backups_valid_artifact",
            "status BETWEEN 1 AND 4 AND ((status = 3 AND size_bytes > 0 AND size_bytes <= 107374182400 " +
            "AND checksum ~ '^[0-9A-F]{64}$' AND size_bytes IS NOT NULL AND checksum IS NOT NULL) OR " +
            "(status <> 3 AND size_bytes IS NULL AND checksum IS NULL))"));
        builder.HasKey(backup => backup.Id).HasName("pk_backups");
        builder.Property(backup => backup.Id).HasColumnName("id");
        builder.Property(backup => backup.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(backup => backup.SizeBytes).HasColumnName("size_bytes");
        builder.Property(backup => backup.Checksum).HasColumnName("checksum").HasMaxLength(64);
        builder.HasOne<ServerCommand>().WithOne().HasForeignKey<Backup>(backup => backup.Id)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_backups_server_commands_id");
    }
}
