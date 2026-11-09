using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quests.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_OwnerId",
                table: "Tasks");

            migrationBuilder.AddColumn<long>(
                name: "SyncVersion",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Tasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_OwnerId_Version",
                table: "Tasks",
                columns: new[] { "OwnerId", "Version" });

            // Existing tasks become version 1, so a first pull (since = 0) returns them.
            migrationBuilder.Sql("UPDATE \"Tasks\" SET \"Version\" = 1;");
            migrationBuilder.Sql("UPDATE \"Users\" SET \"SyncVersion\" = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_OwnerId_Version",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "SyncVersion",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Tasks");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_OwnerId",
                table: "Tasks",
                column: "OwnerId");
        }
    }
}
