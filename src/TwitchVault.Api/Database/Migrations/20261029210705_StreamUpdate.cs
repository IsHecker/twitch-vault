using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TwitchVault.Api.Database.Migrations
{
    /// <inheritdoc />
    public partial class StreamUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StorageLocation",
                table: "Streams");

            migrationBuilder.DropColumn(
                name: "VodCheckAttemptedAt",
                table: "Streams");

            migrationBuilder.AddColumn<bool>(
                name: "IsVodChecked",
                table: "Streams",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsVodChecked",
                table: "Streams");

            migrationBuilder.AddColumn<string>(
                name: "StorageLocation",
                table: "Streams",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "VodCheckAttemptedAt",
                table: "Streams",
                type: "datetime2",
                nullable: true);
        }
    }
}
