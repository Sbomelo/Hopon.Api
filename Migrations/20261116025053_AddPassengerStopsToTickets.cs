using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hopon.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPassengerStopsToTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DestinationTripStopId",
                table: "Tickets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginTripStopId",
                table: "Tickets",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_DestinationTripStopId",
                table: "Tickets",
                column: "DestinationTripStopId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_OriginTripStopId",
                table: "Tickets",
                column: "OriginTripStopId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_TripStops_DestinationTripStopId",
                table: "Tickets",
                column: "DestinationTripStopId",
                principalTable: "TripStops",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_TripStops_OriginTripStopId",
                table: "Tickets",
                column: "OriginTripStopId",
                principalTable: "TripStops",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_TripStops_DestinationTripStopId",
                table: "Tickets");

            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_TripStops_OriginTripStopId",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_DestinationTripStopId",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_OriginTripStopId",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "DestinationTripStopId",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "OriginTripStopId",
                table: "Tickets");
        }
    }
}
