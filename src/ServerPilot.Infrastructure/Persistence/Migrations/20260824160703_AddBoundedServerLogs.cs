using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerPilot.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddBoundedServerLogs : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_server_instances_valid_timestamps",
            table: "server_instances");

        migrationBuilder.AddColumn<string>(
            name: "last_log_chunk_content",
            table: "server_instances",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "last_log_chunk_from_offset",
            table: "server_instances",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "last_log_chunk_reset",
            table: "server_instances",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "last_log_content",
            table: "server_instances",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "last_log_offset",
            table: "server_instances",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "last_log_reported_at",
            table: "server_instances",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "last_log_status",
            table: "server_instances",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "last_log_stream_id",
            table: "server_instances",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_server_instances_valid_logs",
            table: "server_instances",
            sql: "(last_log_status IS NULL AND last_log_reported_at IS NULL AND last_log_content IS NULL AND last_log_stream_id IS NULL AND last_log_offset IS NULL AND last_log_chunk_content IS NULL AND last_log_chunk_from_offset IS NULL AND NOT last_log_chunk_reset) OR (last_log_status IN (1, 2, 3) AND last_log_reported_at IS NOT NULL AND ((last_log_status IN (2, 3) AND last_log_stream_id IS NULL AND last_log_offset IS NULL AND last_log_content IS NULL AND last_log_chunk_content IS NULL AND last_log_chunk_from_offset IS NULL AND NOT last_log_chunk_reset) OR (last_log_stream_id IS NOT NULL AND last_log_offset >= 0 AND last_log_content IS NOT NULL AND octet_length(last_log_content) <= 32768 AND last_log_chunk_content IS NOT NULL AND octet_length(last_log_chunk_content) <= 32768 AND last_log_chunk_from_offset >= 0 AND last_log_chunk_from_offset <= last_log_offset)))");

        migrationBuilder.AddCheckConstraint(
            name: "ck_server_instances_valid_timestamps",
            table: "server_instances",
            sql: "updated_at >= created_at AND (last_status_reported_at IS NULL OR (last_status_reported_at >= created_at AND last_status_reported_at <= updated_at)) AND (last_log_reported_at IS NULL OR (last_log_reported_at >= created_at AND last_log_reported_at <= updated_at))");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_server_instances_valid_logs",
            table: "server_instances");

        migrationBuilder.DropCheckConstraint(
            name: "ck_server_instances_valid_timestamps",
            table: "server_instances");

        migrationBuilder.DropColumn(
            name: "last_log_chunk_content",
            table: "server_instances");

        migrationBuilder.DropColumn(
            name: "last_log_chunk_from_offset",
            table: "server_instances");

        migrationBuilder.DropColumn(
            name: "last_log_chunk_reset",
            table: "server_instances");

        migrationBuilder.DropColumn(
            name: "last_log_content",
            table: "server_instances");

        migrationBuilder.DropColumn(
            name: "last_log_offset",
            table: "server_instances");

        migrationBuilder.DropColumn(
            name: "last_log_reported_at",
            table: "server_instances");

        migrationBuilder.DropColumn(
            name: "last_log_status",
            table: "server_instances");

        migrationBuilder.DropColumn(
            name: "last_log_stream_id",
            table: "server_instances");

        migrationBuilder.AddCheckConstraint(
            name: "ck_server_instances_valid_timestamps",
            table: "server_instances",
            sql: "updated_at >= created_at AND (last_status_reported_at IS NULL OR (last_status_reported_at >= created_at AND last_status_reported_at <= updated_at))");
    }
}
