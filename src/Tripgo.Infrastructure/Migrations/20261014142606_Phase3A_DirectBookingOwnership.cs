using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tripgo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase3A_DirectBookingOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerIdentifier",
                table: "Bookings",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_OwnerIdentifier_CreatedAt",
                table: "Bookings",
                columns: new[] { "OwnerIdentifier", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_PassengerPhone_OwnerIdentifier",
                table: "Bookings",
                columns: new[] { "PassengerPhone", "OwnerIdentifier" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_OwnerIdentifier_CreatedAt",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_PassengerPhone_OwnerIdentifier",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "OwnerIdentifier",
                table: "Bookings");
        }
    }
}
