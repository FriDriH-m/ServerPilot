using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerPilot.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddProjectZomboidServerProfile : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_server_instances_trimmed_configuration",
            table: "server_instances");

        migrationBuilder.AddColumn<string>(
            name: "data_directory",
            table: "server_instances",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "profile",
            table: "server_instances",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddCheckConstraint(
            name: "ck_server_instances_trimmed_configuration",
            table: "server_instances",
            sql: "profile IN (0, 1) AND name = btrim(name) AND name <> '' AND executable_path = btrim(executable_path) AND executable_path <> '' AND arguments = btrim(arguments) AND working_directory = btrim(working_directory) AND working_directory <> '' AND process_name = btrim(process_name) AND process_name <> '' AND position('/' in process_name) = 0 AND position(':' in process_name) = 0 AND ((profile = 0 AND data_directory IS NULL) OR (profile = 1 AND data_directory = btrim(data_directory) AND data_directory <> '' AND arguments = '' AND lower(process_name) = 'java'))");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_server_instances_trimmed_configuration",
            table: "server_instances");

        migrationBuilder.DropColumn(
            name: "data_directory",
            table: "server_instances");

        migrationBuilder.DropColumn(
            name: "profile",
            table: "server_instances");

        migrationBuilder.AddCheckConstraint(
            name: "ck_server_instances_trimmed_configuration",
            table: "server_instances",
            sql: "name = btrim(name) AND name <> '' AND executable_path = btrim(executable_path) AND executable_path <> '' AND arguments = btrim(arguments) AND working_directory = btrim(working_directory) AND working_directory <> '' AND process_name = btrim(process_name) AND process_name <> '' AND position('/' in process_name) = 0 AND position(':' in process_name) = 0");
    }
}
