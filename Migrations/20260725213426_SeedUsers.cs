using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Hopon.Api.Migrations
{
    /// <inheritdoc />
    public partial class SeedUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "CreatedAt", "FullName", "IsActive", "PhoneNumber" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 7, 25, 21, 34, 22, 795, DateTimeKind.Utc).AddTicks(9955), "Sibonelo Vimba", true, "+27789689875" },
                    { 2, new DateTime(2026, 7, 25, 21, 34, 22, 795, DateTimeKind.Utc).AddTicks(9958), "Lumiyo Vimba", true, "+27835944077" },
                    { 3, new DateTime(2026, 7, 25, 21, 34, 22, 795, DateTimeKind.Utc).AddTicks(9961), "Lee-Ann Damane", true, "+27814629100" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3);
        }
    }
}
