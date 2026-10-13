using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tripgo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase3A_BookingSafetyHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BookingSeats_TripSeatId",
                table: "BookingSeats");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "TripSeats",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "BookingSeats",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "HoldToken",
                table: "Bookings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingSeats_TripSeatId",
                table: "BookingSeats",
                column: "TripSeatId",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_HoldToken",
                table: "Bookings",
                column: "HoldToken",
                unique: true,
                filter: "[HoldToken] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BookingSeats_TripSeatId",
                table: "BookingSeats");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_HoldToken",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "TripSeats");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "BookingSeats");

            migrationBuilder.DropColumn(
                name: "HoldToken",
                table: "Bookings");

            migrationBuilder.CreateIndex(
                name: "IX_BookingSeats_TripSeatId",
                table: "BookingSeats",
                column: "TripSeatId");
        }
    }
}
