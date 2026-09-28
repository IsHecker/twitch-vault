using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TwitchVault.Api.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddVodFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "VodCheckAttemptedAt",
                table: "Streams",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VodId",
                table: "Streams",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VodCheckAttemptedAt",
                table: "Streams");

            migrationBuilder.DropColumn(
                name: "VodId",
                table: "Streams");
        }
    }
}
