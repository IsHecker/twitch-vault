using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TwitchVault.Api.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class SafeChannelDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Streams_Channels_ChannelId",
                table: "Streams");

            migrationBuilder.AlterColumn<string>(
                name: "ChannelId",
                table: "Streams",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddForeignKey(
                name: "FK_Streams_Channels_ChannelId",
                table: "Streams",
                column: "ChannelId",
                principalTable: "Channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Streams_Channels_ChannelId",
                table: "Streams");

            migrationBuilder.AlterColumn<string>(
                name: "ChannelId",
                table: "Streams",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Streams_Channels_ChannelId",
                table: "Streams",
                column: "ChannelId",
                principalTable: "Channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
