using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tripgo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage1To4_ProductionFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BookingConfirmationEmailOutbox_BookingId",
                table: "BookingConfirmationEmailOutbox");

            migrationBuilder.AddColumn<long>(
                name: "OperatorId",
                table: "Users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Trips",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DataSource",
                table: "Trips",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceReference",
                table: "Trips",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DataSource",
                table: "Routes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceReference",
                table: "Routes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "PaymentTransactions",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "VND");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "PaymentTransactions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "PaymentTransactions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "SANDBOX");

            migrationBuilder.AddColumn<string>(
                name: "RefundReference",
                table: "PaymentTransactions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RefundedAt",
                table: "PaymentTransactions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DataSource",
                table: "Operators",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceReference",
                table: "Operators",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationEvidence",
                table: "Operators",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VerifiedAt",
                table: "Operators",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "VerifiedByUserId",
                table: "Operators",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DedupKey",
                table: "BookingConfirmationEmailOutbox",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MessageType",
                table: "BookingConfirmationEmailOutbox",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "BookingConfirmation");

            migrationBuilder.CreateTable(
                name: "AdminAuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorUserId = table.Column<long>(type: "bigint", nullable: true),
                    ActorEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TransactionCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SignatureValid = table.Column<bool>(type: "bit", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReportedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ReportedCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentWebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TicketCredentials",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<long>(type: "bigint", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProtectedToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsSandbox = table.Column<bool>(type: "bit", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BoardedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    BoardedByUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketCredentials_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TicketVerificationAttempts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TicketCredentialId = table.Column<long>(type: "bigint", nullable: true),
                    TokenFingerprint = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Result = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ActorUserId = table.Column<long>(type: "bigint", nullable: true),
                    ActorKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketVerificationAttempts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_BookingId_Paid",
                table: "PaymentTransactions",
                column: "BookingId",
                unique: true,
                filter: "[Status] = 2");

            migrationBuilder.CreateIndex(
                name: "IX_BookingConfirmationEmailOutbox_BookingId_MessageType_DedupKey",
                table: "BookingConfirmationEmailOutbox",
                columns: new[] { "BookingId", "MessageType", "DedupKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_CreatedAt",
                table: "AdminAuditLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_EntityType_EntityId",
                table: "AdminAuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookEvents_Provider_EventId",
                table: "PaymentWebhookEvents",
                columns: new[] { "Provider", "EventId" },
                unique: true,
                filter: "[EventId] IS NOT NULL AND [SignatureValid] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookEvents_ReceivedAt",
                table: "PaymentWebhookEvents",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TicketCredentials_BookingId",
                table: "TicketCredentials",
                column: "BookingId",
                unique: true,
                filter: "[RevokedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TicketCredentials_TokenHash",
                table: "TicketCredentials",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TicketVerificationAttempts_ActorKey_AttemptedAt",
                table: "TicketVerificationAttempts",
                columns: new[] { "ActorKey", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TicketVerificationAttempts_TicketCredentialId",
                table: "TicketVerificationAttempts",
                column: "TicketCredentialId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminAuditLogs");

            migrationBuilder.DropTable(
                name: "PaymentWebhookEvents");

            migrationBuilder.DropTable(
                name: "TicketCredentials");

            migrationBuilder.DropTable(
                name: "TicketVerificationAttempts");

            migrationBuilder.DropIndex(
                name: "IX_PaymentTransactions_BookingId_Paid",
                table: "PaymentTransactions");

            migrationBuilder.DropIndex(
                name: "IX_BookingConfirmationEmailOutbox_BookingId_MessageType_DedupKey",
                table: "BookingConfirmationEmailOutbox");

            migrationBuilder.DropColumn(
                name: "OperatorId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "DataSource",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "SourceReference",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "DataSource",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "SourceReference",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "RefundReference",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "RefundedAt",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "DataSource",
                table: "Operators");

            migrationBuilder.DropColumn(
                name: "SourceReference",
                table: "Operators");

            migrationBuilder.DropColumn(
                name: "VerificationEvidence",
                table: "Operators");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "Operators");

            migrationBuilder.DropColumn(
                name: "VerifiedByUserId",
                table: "Operators");

            migrationBuilder.DropColumn(
                name: "DedupKey",
                table: "BookingConfirmationEmailOutbox");

            migrationBuilder.DropColumn(
                name: "MessageType",
                table: "BookingConfirmationEmailOutbox");

            migrationBuilder.CreateIndex(
                name: "IX_BookingConfirmationEmailOutbox_BookingId",
                table: "BookingConfirmationEmailOutbox",
                column: "BookingId",
                unique: true);
        }
    }
}
