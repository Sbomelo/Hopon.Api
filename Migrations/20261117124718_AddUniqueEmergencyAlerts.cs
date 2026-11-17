using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hopon.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueEmergencyAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_UserId_TripId_Type",
                table: "NotificationLogs",
                columns: new[] { "UserId", "TripId", "Type" },
                unique: true,
                filter: "[TripId] IS NOT NULL AND [Type] = 6");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NotificationLogs_UserId_TripId_Type",
                table: "NotificationLogs");
        }
    }
}
