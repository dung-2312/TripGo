using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tripgo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase3A_PersistentBookingCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDemoTrip",
                table: "Trips",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Bookings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TripId = table.Column<long>(type: "bigint", nullable: false),
                    PickupPointId = table.Column<long>(type: "bigint", nullable: true),
                    DropoffPointId = table.Column<long>(type: "bigint", nullable: true),
                    PickupPointNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DropoffPointNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    PassengerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PassengerPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PassengerEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BaseAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SurchargeAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    HasInsurance = table.Column<bool>(type: "bit", nullable: false),
                    InsuranceAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PaymentStatus = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsDemoBooking = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bookings_PickupDropoffPoints_DropoffPointId",
                        column: x => x.DropoffPointId,
                        principalTable: "PickupDropoffPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bookings_PickupDropoffPoints_PickupPointId",
                        column: x => x.PickupPointId,
                        principalTable: "PickupDropoffPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bookings_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bookings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TripSeats",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TripId = table.Column<long>(type: "bigint", nullable: false),
                    SeatLayoutItemId = table.Column<long>(type: "bigint", nullable: true),
                    SeatCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SeatType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Floor = table.Column<int>(type: "int", nullable: false),
                    Row = table.Column<int>(type: "int", nullable: false),
                    Column = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripSeats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TripSeats_SeatLayoutItems_SeatLayoutItemId",
                        column: x => x.SeatLayoutItemId,
                        principalTable: "SeatLayoutItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TripSeats_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BookingPassengers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<long>(type: "bigint", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IdentityCard = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsPrimaryContact = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingPassengers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingPassengers_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookingSeats",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<long>(type: "bigint", nullable: false),
                    TripSeatId = table.Column<long>(type: "bigint", nullable: false),
                    SeatCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingSeats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingSeats_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookingSeats_TripSeats_TripSeatId",
                        column: x => x.TripSeatId,
                        principalTable: "TripSeats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeatHolds",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TripSeatId = table.Column<long>(type: "bigint", nullable: false),
                    HoldToken = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    HeldAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeatHolds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeatHolds_TripSeats_TripSeatId",
                        column: x => x.TripSeatId,
                        principalTable: "TripSeats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingPassengers_BookingId",
                table: "BookingPassengers",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_BookingCode",
                table: "Bookings",
                column: "BookingCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_DropoffPointId",
                table: "Bookings",
                column: "DropoffPointId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_PassengerPhone_CreatedAt",
                table: "Bookings",
                columns: new[] { "PassengerPhone", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_PickupPointId",
                table: "Bookings",
                column: "PickupPointId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_TripId_Status",
                table: "Bookings",
                columns: new[] { "TripId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_UserId",
                table: "Bookings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingSeats_BookingId_TripSeatId",
                table: "BookingSeats",
                columns: new[] { "BookingId", "TripSeatId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingSeats_TripSeatId",
                table: "BookingSeats",
                column: "TripSeatId");

            migrationBuilder.CreateIndex(
                name: "IX_SeatHolds_HoldToken_Status",
                table: "SeatHolds",
                columns: new[] { "HoldToken", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SeatHolds_TripSeatId_Status_ExpiresAt",
                table: "SeatHolds",
                columns: new[] { "TripSeatId", "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TripSeats_SeatLayoutItemId",
                table: "TripSeats",
                column: "SeatLayoutItemId");

            migrationBuilder.CreateIndex(
                name: "IX_TripSeats_TripId_SeatCode",
                table: "TripSeats",
                columns: new[] { "TripId", "SeatCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TripSeats_TripId_Status",
                table: "TripSeats",
                columns: new[] { "TripId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingPassengers");

            migrationBuilder.DropTable(
                name: "BookingSeats");

            migrationBuilder.DropTable(
                name: "SeatHolds");

            migrationBuilder.DropTable(
                name: "Bookings");

            migrationBuilder.DropTable(
                name: "TripSeats");

            migrationBuilder.DropColumn(
                name: "IsDemoTrip",
                table: "Trips");
        }
    }
}
