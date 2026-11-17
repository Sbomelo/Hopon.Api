using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hopon.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationChannelUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NotificationLogs_UserId_TripId_Type",
                table: "NotificationLogs");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_UserId_TripId_Type_Channel",
                table: "NotificationLogs",
                columns: new[] { "UserId", "TripId", "Type", "Channel" },
                unique: true,
                filter: "[TripId] IS NOT NULL AND [Type] IN (0, 1, 2, 3, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NotificationLogs_UserId_TripId_Type_Channel",
                table: "NotificationLogs");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_UserId_TripId_Type",
                table: "NotificationLogs",
                columns: new[] { "UserId", "TripId", "Type" },
                unique: true,
                filter: "[TripId] IS NOT NULL AND [Type] IN (0, 1, 2, 3, 4)");
        }
    }
}
