using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TwitchVault.Api.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class Subscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "GoogleId",
                table: "Users",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ChannelName",
                table: "BannedChannels",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "BannedChannels",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.RenameTable(
                name: "UserChannels",
                newName: "Subscriptions");

            migrationBuilder.RenameIndex(
                name: "IX_UserChannels_ChannelId",
                table: "Subscriptions",
                newName: "IX_Subscriptions_ChannelId");

            migrationBuilder.Sql("EXEC sp_rename 'FK_UserChannels_Channels_ChannelId', 'FK_Subscriptions_Channels_ChannelId'");
            migrationBuilder.Sql("EXEC sp_rename 'FK_UserChannels_Users_UserId', 'FK_Subscriptions_Users_UserId'");

            migrationBuilder.CreateIndex(
                name: "IX_Users_GoogleId",
                table: "Users",
                column: "GoogleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BannedChannels_ChannelName",
                table: "BannedChannels",
                column: "ChannelName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_GoogleId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_BannedChannels_ChannelName",
                table: "BannedChannels");

            migrationBuilder.Sql("EXEC sp_rename 'PK_Subscriptions', 'PK_UserChannels'");
            migrationBuilder.Sql("EXEC sp_rename 'FK_Subscriptions_Channels_ChannelId', 'FK_UserChannels_Channels_ChannelId'");
            migrationBuilder.Sql("EXEC sp_rename 'FK_Subscriptions_Users_UserId', 'FK_UserChannels_Users_UserId'");

            migrationBuilder.RenameIndex(
                name: "IX_Subscriptions_ChannelId",
                table: "UserChannels",
                newName: "IX_UserChannels_ChannelId");

            migrationBuilder.RenameTable(
                name: "Subscriptions",
                newName: "UserChannels");

            migrationBuilder.AlterColumn<string>(
                name: "GoogleId",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ChannelName",
                table: "BannedChannels",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "BannedChannels",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}