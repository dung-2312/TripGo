using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tripgo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixDatabaseFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Drivers_Operators_OperatorId",
                table: "Drivers");

            migrationBuilder.DropForeignKey(
                name: "FK_PickupDropoffPoints_Locations_LocationId",
                table: "PickupDropoffPoints");

            migrationBuilder.DropForeignKey(
                name: "FK_PickupDropoffPoints_Operators_OperatorId",
                table: "PickupDropoffPoints");

            migrationBuilder.DropForeignKey(
                name: "FK_RouteStops_PickupDropoffPoints_PointId",
                table: "RouteStops");

            migrationBuilder.DropForeignKey(
                name: "FK_TripDrivers_Drivers_DriverId",
                table: "TripDrivers");

            migrationBuilder.DropForeignKey(
                name: "FK_Trips_Routes_RouteId1",
                table: "Trips");

            migrationBuilder.DropForeignKey(
                name: "FK_TripStops_PickupDropoffPoints_PointId",
                table: "TripStops");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Roles_RoleId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_TripStops_TripId",
                table: "TripStops");

            migrationBuilder.DropIndex(
                name: "IX_Trips_RouteId1",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_TripDrivers_TripId",
                table: "TripDrivers");

            migrationBuilder.DropIndex(
                name: "IX_RouteStops_RouteId",
                table: "RouteStops");

            migrationBuilder.DropIndex(
                name: "IX_Routes_OperatorId",
                table: "Routes");

            migrationBuilder.DropIndex(
                name: "IX_Drivers_OperatorId",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "RouteId1",
                table: "Trips");

            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "Users",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AlterColumn<string>(
                name: "FullName",
                table: "Users",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "TripCode",
                table: "Trips",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Routes",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Routes",
                type: "bit",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Routes",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Roles",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Roles",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "PickupDropoffPoints",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "PickupDropoffPoints",
                type: "bit",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AlterColumn<string>(
                name: "Address",
                table: "PickupDropoffPoints",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                table: "Drivers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "LicenseNumber",
                table: "Drivers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "LicenseClass",
                table: "Drivers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Drivers",
                type: "bit",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AlterColumn<string>(
                name: "FullName",
                table: "Drivers",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_PhoneNumber",
                table: "Users",
                column: "PhoneNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TripStops_TripId_StopOrder",
                table: "TripStops",
                columns: new[] { "TripId", "StopOrder" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TripStops_StopOrder",
                table: "TripStops",
                sql: "[StopOrder] > 0");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_DepartureTime_Status",
                table: "Trips",
                columns: new[] { "DepartureTime", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_TripCode",
                table: "Trips",
                column: "TripCode",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Trips_Arrival_After_Departure",
                table: "Trips",
                sql: "[ExpectedArrivalTime] > [DepartureTime]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Trips_BasePrice",
                table: "Trips",
                sql: "[BasePrice] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_TripDrivers_TripId_DriverId",
                table: "TripDrivers",
                columns: new[] { "TripId", "DriverId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RouteStops_RouteId_StopOrder",
                table: "RouteStops",
                columns: new[] { "RouteId", "StopOrder" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_RouteStops_StopOrder",
                table: "RouteStops",
                sql: "[StopOrder] > 0");

            migrationBuilder.CreateIndex(
                name: "IX_Routes_OperatorId_Code",
                table: "Routes",
                columns: new[] { "OperatorId", "Code" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Routes_DistanceKm",
                table: "Routes",
                sql: "[DistanceKm] IS NULL OR [DistanceKm] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Routes_EstimatedDurationMinutes",
                table: "Routes",
                sql: "[EstimatedDurationMinutes] IS NULL OR [EstimatedDurationMinutes] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Routes_Origin_Destination_Different",
                table: "Routes",
                sql: "[OriginLocationId] <> [DestinationLocationId]");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PickupDropoffPoints_Latitude",
                table: "PickupDropoffPoints",
                sql: "[Latitude] IS NULL OR ([Latitude] >= -90.0 AND [Latitude] <= 90.0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PickupDropoffPoints_Longitude",
                table: "PickupDropoffPoints",
                sql: "[Longitude] IS NULL OR ([Longitude] >= -180.0 AND [Longitude] <= 180.0)");

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_LicenseNumber",
                table: "Drivers",
                column: "LicenseNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_OperatorId_LicenseNumber",
                table: "Drivers",
                columns: new[] { "OperatorId", "LicenseNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Drivers_Operators_OperatorId",
                table: "Drivers",
                column: "OperatorId",
                principalTable: "Operators",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PickupDropoffPoints_Locations_LocationId",
                table: "PickupDropoffPoints",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PickupDropoffPoints_Operators_OperatorId",
                table: "PickupDropoffPoints",
                column: "OperatorId",
                principalTable: "Operators",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RouteStops_PickupDropoffPoints_PointId",
                table: "RouteStops",
                column: "PointId",
                principalTable: "PickupDropoffPoints",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TripDrivers_Drivers_DriverId",
                table: "TripDrivers",
                column: "DriverId",
                principalTable: "Drivers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TripStops_PickupDropoffPoints_PointId",
                table: "TripStops",
                column: "PointId",
                principalTable: "PickupDropoffPoints",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Roles_RoleId",
                table: "Users",
                column: "RoleId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Drivers_Operators_OperatorId",
                table: "Drivers");

            migrationBuilder.DropForeignKey(
                name: "FK_PickupDropoffPoints_Locations_LocationId",
                table: "PickupDropoffPoints");

            migrationBuilder.DropForeignKey(
                name: "FK_PickupDropoffPoints_Operators_OperatorId",
                table: "PickupDropoffPoints");

            migrationBuilder.DropForeignKey(
                name: "FK_RouteStops_PickupDropoffPoints_PointId",
                table: "RouteStops");

            migrationBuilder.DropForeignKey(
                name: "FK_TripDrivers_Drivers_DriverId",
                table: "TripDrivers");

            migrationBuilder.DropForeignKey(
                name: "FK_TripStops_PickupDropoffPoints_PointId",
                table: "TripStops");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Roles_RoleId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_PhoneNumber",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_TripStops_TripId_StopOrder",
                table: "TripStops");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TripStops_StopOrder",
                table: "TripStops");

            migrationBuilder.DropIndex(
                name: "IX_Trips_DepartureTime_Status",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Trips_TripCode",
                table: "Trips");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Trips_Arrival_After_Departure",
                table: "Trips");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Trips_BasePrice",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_TripDrivers_TripId_DriverId",
                table: "TripDrivers");

            migrationBuilder.DropIndex(
                name: "IX_RouteStops_RouteId_StopOrder",
                table: "RouteStops");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RouteStops_StopOrder",
                table: "RouteStops");

            migrationBuilder.DropIndex(
                name: "IX_Routes_OperatorId_Code",
                table: "Routes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Routes_DistanceKm",
                table: "Routes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Routes_EstimatedDurationMinutes",
                table: "Routes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Routes_Origin_Destination_Different",
                table: "Routes");

            migrationBuilder.DropIndex(
                name: "IX_Roles_Name",
                table: "Roles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PickupDropoffPoints_Latitude",
                table: "PickupDropoffPoints");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PickupDropoffPoints_Longitude",
                table: "PickupDropoffPoints");

            migrationBuilder.DropIndex(
                name: "IX_Drivers_LicenseNumber",
                table: "Drivers");

            migrationBuilder.DropIndex(
                name: "IX_Drivers_OperatorId_LicenseNumber",
                table: "Drivers");

            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Users",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<string>(
                name: "FullName",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(254)",
                oldMaxLength: 254);

            migrationBuilder.AlterColumn<string>(
                name: "TripCode",
                table: "Trips",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<long>(
                name: "RouteId1",
                table: "Trips",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Routes",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Routes",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Routes",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Roles",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Roles",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(250)",
                oldMaxLength: 250,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "PickupDropoffPoints",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "PickupDropoffPoints",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<string>(
                name: "Address",
                table: "PickupDropoffPoints",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(300)",
                oldMaxLength: 300);

            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                table: "Drivers",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "LicenseNumber",
                table: "Drivers",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "LicenseClass",
                table: "Drivers",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Drivers",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<string>(
                name: "FullName",
                table: "Drivers",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(150)",
                oldMaxLength: 150);

            migrationBuilder.CreateIndex(
                name: "IX_TripStops_TripId",
                table: "TripStops",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_RouteId1",
                table: "Trips",
                column: "RouteId1");

            migrationBuilder.CreateIndex(
                name: "IX_TripDrivers_TripId",
                table: "TripDrivers",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteStops_RouteId",
                table: "RouteStops",
                column: "RouteId");

            migrationBuilder.CreateIndex(
                name: "IX_Routes_OperatorId",
                table: "Routes",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_OperatorId",
                table: "Drivers",
                column: "OperatorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Drivers_Operators_OperatorId",
                table: "Drivers",
                column: "OperatorId",
                principalTable: "Operators",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PickupDropoffPoints_Locations_LocationId",
                table: "PickupDropoffPoints",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PickupDropoffPoints_Operators_OperatorId",
                table: "PickupDropoffPoints",
                column: "OperatorId",
                principalTable: "Operators",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RouteStops_PickupDropoffPoints_PointId",
                table: "RouteStops",
                column: "PointId",
                principalTable: "PickupDropoffPoints",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TripDrivers_Drivers_DriverId",
                table: "TripDrivers",
                column: "DriverId",
                principalTable: "Drivers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Trips_Routes_RouteId1",
                table: "Trips",
                column: "RouteId1",
                principalTable: "Routes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_TripStops_PickupDropoffPoints_PointId",
                table: "TripStops",
                column: "PointId",
                principalTable: "PickupDropoffPoints",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Roles_RoleId",
                table: "Users",
                column: "RoleId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
