using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerPilot.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddServerInstanceProcessStateReconciliation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_server_instances_valid_state",
            table: "server_instances");

        migrationBuilder.DropCheckConstraint(
            name: "ck_server_instances_valid_timestamps",
            table: "server_instances");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "last_process_started_at",
            table: "server_instances",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "last_status_reported_at",
            table: "server_instances",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql(
            "UPDATE server_instances SET status = 1, last_process_id = NULL " +
            "WHERE status <> 1 OR last_process_id IS NOT NULL;");

        migrationBuilder.AddCheckConstraint(
            name: "ck_server_instances_valid_state",
            table: "server_instances",
            sql: "(status = 1 AND last_status_reported_at IS NULL AND last_process_id IS NULL AND last_process_started_at IS NULL) OR (status = 3 AND last_status_reported_at IS NOT NULL AND last_process_id > 0 AND last_process_started_at IS NOT NULL) OR (status IN (5, 6) AND last_status_reported_at IS NOT NULL AND last_process_id IS NULL AND last_process_started_at IS NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_server_instances_valid_timestamps",
            table: "server_instances",
            sql: "updated_at >= created_at AND (last_status_reported_at IS NULL OR (last_status_reported_at >= created_at AND last_status_reported_at <= updated_at))");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_server_instances_valid_state",
            table: "server_instances");

        migrationBuilder.DropCheckConstraint(
            name: "ck_server_instances_valid_timestamps",
            table: "server_instances");

        migrationBuilder.DropColumn(
            name: "last_process_started_at",
            table: "server_instances");

        migrationBuilder.DropColumn(
            name: "last_status_reported_at",
            table: "server_instances");

        migrationBuilder.AddCheckConstraint(
            name: "ck_server_instances_valid_state",
            table: "server_instances",
            sql: "status BETWEEN 1 AND 7 AND (last_process_id IS NULL OR last_process_id > 0)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_server_instances_valid_timestamps",
            table: "server_instances",
            sql: "updated_at >= created_at");
    }
}
