using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tripgo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase3A_HoldOwnershipReleaseGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerIdentifier",
                table: "SeatHolds",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeatHolds_HoldToken_OwnerIdentifier",
                table: "SeatHolds",
                columns: new[] { "HoldToken", "OwnerIdentifier" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SeatHolds_HoldToken_OwnerIdentifier",
                table: "SeatHolds");

            migrationBuilder.DropColumn(
                name: "OwnerIdentifier",
                table: "SeatHolds");
        }
    }
}
