using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hopon.Api.Migrations
{
    /// <inheritdoc />
    public partial class SeededANewDriver : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "Drivers",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "Drivers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.InsertData(
                table: "Drivers",
                columns: new[] { "Id", "CreatedAt", "FullName", "IsActive", "PasswordHash", "PhoneNumber", "Username" },
                values: new object[] { 2, new DateTime(2026, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), "Lumiyo V.", true, "$2b$11$UuSLULVipvBFekLPyiR5/uskliTxsDk7X0gnK4gkjzMQaF703w.0K", "+27821234567", "driver1" });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "CreatedAt",
                value: new DateTime(2026, 8, 4, 13, 1, 46, 336, DateTimeKind.Utc).AddTicks(9378));

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 2,
                column: "CreatedAt",
                value: new DateTime(2026, 8, 4, 13, 1, 46, 336, DateTimeKind.Utc).AddTicks(9382));

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3,
                column: "CreatedAt",
                value: new DateTime(2026, 8, 4, 13, 1, 46, 336, DateTimeKind.Utc).AddTicks(9384));

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_Username",
                table: "Drivers",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Drivers_Username",
                table: "Drivers");

            migrationBuilder.DeleteData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DropColumn(
                name: "PasswordHash",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "Drivers");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "CreatedAt",
                value: new DateTime(2026, 7, 25, 21, 34, 22, 795, DateTimeKind.Utc).AddTicks(9955));

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 2,
                column: "CreatedAt",
                value: new DateTime(2026, 7, 25, 21, 34, 22, 795, DateTimeKind.Utc).AddTicks(9958));

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3,
                column: "CreatedAt",
                value: new DateTime(2026, 7, 25, 21, 34, 22, 795, DateTimeKind.Utc).AddTicks(9961));
        }
    }
}
