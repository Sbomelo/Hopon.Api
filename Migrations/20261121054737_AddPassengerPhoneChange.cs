using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hopon.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPassengerPhoneChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PhoneChangeUserId",
                table: "OtpRequests",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OtpRequests_PhoneChangeUserId_CreatedAt",
                table: "OtpRequests",
                columns: new[] { "PhoneChangeUserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OtpRequests_PhoneChangeUserId_CreatedAt",
                table: "OtpRequests");

            migrationBuilder.DropColumn(
                name: "PhoneChangeUserId",
                table: "OtpRequests");
        }
    }
}
