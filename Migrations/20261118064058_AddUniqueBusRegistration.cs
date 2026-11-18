using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hopon.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueBusRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RegistrationNumber",
                table: "Buses",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_Buses_RegistrationNumber",
                table: "Buses",
                column: "RegistrationNumber",
                unique: true,
                filter: "[RegistrationNumber] <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Buses_RegistrationNumber",
                table: "Buses");

            migrationBuilder.AlterColumn<string>(
                name: "RegistrationNumber",
                table: "Buses",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
