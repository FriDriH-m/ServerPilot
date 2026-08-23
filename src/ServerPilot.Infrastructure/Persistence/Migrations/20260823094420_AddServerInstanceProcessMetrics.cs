using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerPilot.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddServerInstanceProcessMetrics : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "last_cpu_usage_percent",
            table: "server_instances",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "last_metrics_reported_at",
            table: "server_instances",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "last_uptime_seconds",
            table: "server_instances",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "last_working_set_bytes",
            table: "server_instances",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_server_instances_valid_metrics",
            table: "server_instances",
            sql: "(last_metrics_reported_at IS NULL AND last_cpu_usage_percent IS NULL AND last_working_set_bytes IS NULL AND last_uptime_seconds IS NULL) OR (status = 3 AND last_metrics_reported_at IS NOT NULL AND last_metrics_reported_at <= last_status_reported_at AND (last_cpu_usage_percent IS NULL OR last_cpu_usage_percent BETWEEN 0 AND 100) AND last_working_set_bytes >= 0 AND last_uptime_seconds >= 0)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_server_instances_valid_metrics",
            table: "server_instances");

        migrationBuilder.DropColumn(
            name: "last_cpu_usage_percent",
            table: "server_instances");

        migrationBuilder.DropColumn(
            name: "last_metrics_reported_at",
            table: "server_instances");

        migrationBuilder.DropColumn(
            name: "last_uptime_seconds",
            table: "server_instances");

        migrationBuilder.DropColumn(
            name: "last_working_set_bytes",
            table: "server_instances");
    }
}
