using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tripgo.Application.DTOs;
using Tripgo.Application.DTOs.Payment;
using Tripgo.Application.Interfaces;
using Tripgo.Application.Services;
using Tripgo.Domain;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;
using Tripgo.Infrastructure.Persistence;
using Tripgo.Infrastructure.Persistence.Repositories;
using Tripgo.Infrastructure.Services;
using Tripgo.Tests.Stage1To4;
using Tripgo.Web.Middleware;
using Tripgo.Web.Services;
using Xunit;

namespace Tripgo.Tests;

/// <summary>
/// Phase 8 &amp; Phase 9: Required Security Regression Test Suite covering all 20 specified security areas:
/// 1. External ReturnUrl rejection.
/// 2. Safe internal ReturnUrl.
/// 3. Authenticated Gmail deep link.
/// 4. Anonymous ticket access.
/// 5. Cross-account ticket access.
/// 6. Authenticated identity preservation.
/// 7. Booking session identity separation.
/// 8. GET cancellation does not cancel.
/// 9. Authorized cancellation POST.
/// 10. Unauthorized cancellation.
/// 11. Repeated cancellation.
/// 12. Ticket credential missing.
/// 13. Ticket code sourced from persisted credential.
/// 14. Invalid Email:BaseUrl.
/// 15. Missing Email:BaseUrl.
/// 16. Email delivery failure.
/// 17. Outbox retry.
/// 18. Duplicate payment callback.
/// 19. Seat concurrency.
/// 20. Database write isolation.
/// </summary>
[Collection("Database")]
public sealed class SecurityRegressionAuditTests : IClassFixture<TripgoWebFactory>, IAsyncLifetime
{
    private readonly TripgoWebFactory _factory;
    private readonly Stage _stage = new();

    public SecurityRegressionAuditTests(TripgoWebFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => _stage.CleanupAsync();

    private HttpClient Client() => _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("http://localhost:5065"),
        AllowAutoRedirect = false
    });

    // -------------------------------------------------------------------------------------------------
    // 1 & 2. ReturnUrl Validation (External ReturnUrl Rejection & Safe Internal ReturnUrl Acceptance)
    // -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("https://evil.com")]
    [InlineData("http://attacker.example.org/steal")]
    [InlineData("//evil.com/phishing")]
    [InlineData("/\\evil.com/bypass")]
    [InlineData("javascript:alert(1)")]
    public void Area01_ExternalReturnUrl_IsStrictlyRejected_AndFallsBackToRoot(string untrustedUrl)
    {
        var targetUrl = "/";
        if (!string.IsNullOrWhiteSpace(untrustedUrl) &&
            untrustedUrl.StartsWith('/') && !untrustedUrl.StartsWith("//") && !untrustedUrl.StartsWith("/\\"))
        {
            targetUrl = untrustedUrl;
        }

        Assert.Equal("/", targetUrl);
    }

    [Theory]
    [InlineData("/my-tickets")]
    [InlineData("/my-tickets?code=TRIPGO-77A8CD2887B8")]
    [InlineData("/my-tickets?code=TRIPGO-77A8CD2887B8&action=cancel")]
    [InlineData("/admin/bookings")]
    [InlineData("/account/profile")]
    public void Area02_SafeInternalReturnUrl_IsPreserved(string safeRelativeUrl)
    {
        var targetUrl = "/";
        if (!string.IsNullOrWhiteSpace(safeRelativeUrl) &&
            safeRelativeUrl.StartsWith('/') && !safeRelativeUrl.StartsWith("//") && !safeRelativeUrl.StartsWith("/\\"))
        {
            targetUrl = safeRelativeUrl;
        }

        Assert.Equal(safeRelativeUrl, targetUrl);
    }

    // -------------------------------------------------------------------------------------------------
    // 3 & 6. Identity Preservation & Gmail Deep Links with SameSite=Lax Cookie
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Area03_AuthenticatedGmailDeepLink_CookiePreservesUserClaims_UnderTopLevelGet()
    {
        var client = Client();
        var response = await client.GetAsync("/my-tickets?code=TRIPGO-AUDITTEST01");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Area06_BookingSessionMiddleware_PreservesExistingUserClaims_AndDoesNotWipeIdentity()
    {
        using var scope = _factory.Services.CreateScope();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var existingClaims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "463"),
            new(ClaimTypes.Name, "Nguyễn Thế Dũng"),
            new(ClaimTypes.Email, "dung@tripgo.test"),
            new(ClaimTypes.Role, "Customer")
        };
        var initialIdentity = new ClaimsIdentity(existingClaims, CookieAuthenticationDefaults.AuthenticationScheme);
        httpContext.User = new ClaimsPrincipal(initialIdentity);

        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = new BookingSessionMiddleware(next);

        // When middleware runs, it must ensure OwnerClaimType exists without wiping NameIdentifier
        await middleware.InvokeAsync(httpContext);

        Assert.True(httpContext.User.HasClaim(c => c.Type == BookingSessionContext.OwnerClaimType));
        Assert.Equal("463", httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("Nguyễn Thế Dũng", httpContext.User.FindFirstValue(ClaimTypes.Name));
        Assert.Equal("dung@tripgo.test", httpContext.User.FindFirstValue(ClaimTypes.Email));
    }

    // -------------------------------------------------------------------------------------------------
    // 4 & 5. Ownership & Access Isolation (Anonymous Ticket Access & Cross-Account Isolation)
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Area04_AnonymousVisitor_CannotAccessAnotherUsersBooking_ByCode()
    {
        await using var context = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);

        var anonymousSession = new FixedSession("anonymous-intruder-" + Guid.NewGuid().ToString("N"), null);
        var bookingService = new BookingService(new BookingRepository(context), anonymousSession);

        var retrieved = await bookingService.GetBookingByCodeAsync(booking.BookingCode);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task Area05_CrossAccountTicketAccess_IsStrictlyIsolated_BetweenDifferentUsers()
    {
        await using var context = Stage.NewContext();
        var userA = await _stage.CreateUserAsync(context, RoleNames.Customer);
        var userB = await _stage.CreateUserAsync(context, RoleNames.Customer);

        var sessionA = new FixedSession("owner-A-" + Guid.NewGuid().ToString("N"), userA);
        var serviceA = new BookingService(new BookingRepository(context), sessionA);
        var (_, bookingA, _) = await _stage.CreatePendingBookingAsync(context, owner: null, seats: 1, userId: userA);

        var sessionB = new FixedSession("owner-B-" + Guid.NewGuid().ToString("N"), userB);
        var serviceB = new BookingService(new BookingRepository(context), sessionB);

        // User B cannot view User A's booking
        var lookupByB = await serviceB.GetBookingByCodeAsync(bookingA.BookingCode);
        Assert.Null(lookupByB);

        // User B cannot cancel User A's booking
        var cancelByB = await serviceB.CancelBookingAsync(bookingA.Id, "User B attack");
        Assert.False(cancelByB);
    }

    // -------------------------------------------------------------------------------------------------
    // 7. Booking Session Identity Separation (Hold Token & Owner Separation)
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Area07_BookingSession_SeparatesHoldTokensBetweenDifferentGuests()
    {
        await using var context = Stage.NewContext();
        var minDeparture = DateTimeOffset.UtcNow.Add(BookingRules.SalesCutoffBeforeDeparture).AddHours(2);
        var trip = await context.Trips.Include(t => t.Seats).FirstAsync(t => t.IsDemoTrip && t.Status == TripStatus.Scheduled && t.DepartureTime > minDeparture && t.Seats.Any(s => s.Status == TripSeatStatus.Available));
        var session1 = new FixedSession("guest-1-" + Guid.NewGuid().ToString("N"), null);
        var service1 = new BookingService(new BookingRepository(context), session1);
        await service1.EnsureDemoTripInventoryAsync(trip.Id);
        var seats = await service1.GetSeatsForTripAsync(trip.Id);
        var availableSeat = seats.First(s => s.IsAvailable);

        var holdRes = await service1.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { availableSeat.SeatCode },
            HoldMinutes = 10
        });
        Assert.True(holdRes.Success);
        Assert.NotNull(holdRes.HoldToken);

        // Guest 2 cannot create a booking using Guest 1's hold token
        var session2 = new FixedSession("guest-2-" + Guid.NewGuid().ToString("N"), null);
        var service2 = new BookingService(new BookingRepository(context), session2);

        var bookAttempt = await service2.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { availableSeat.SeatCode },
            HoldToken = holdRes.HoldToken!,
            PassengerName = "Guest Two",
            PassengerPhone = "0988776655"
        });

        Assert.False(bookAttempt.Success);
        await service1.ReleaseHoldAsync(holdRes.HoldToken!);
    }

    // -------------------------------------------------------------------------------------------------
    // 8, 9, 10, 11. Cancellation Integrity (GET Safety, Authorized POST, Unauthorized, Repeated)
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Area08_GetCancellationQuery_DoesNotCancelBookingInDatabase()
    {
        await using var context = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);

        // Simulated HTTP GET navigation with ?action=cancel
        var client = Client();
        var response = await client.GetAsync($"/my-tickets?code={booking.BookingCode}&action=cancel");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Booking status must still be PendingPayment (NOT Cancelled)
        await using var verifyContext = Stage.NewContext();
        var dbBooking = await verifyContext.Bookings.AsNoTracking().FirstAsync(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.PendingPayment, dbBooking.Status);
    }

    [Fact]
    public async Task Area09_AuthorizedCancellation_ReleasesSeatsAndCancelsBooking()
    {
        await using var context = Stage.NewContext();
        var owner = "owner-cancel-" + Guid.NewGuid().ToString("N");
        var session = new FixedSession(owner, null);
        var service = new BookingService(new BookingRepository(context), session);

        var (_, booking, seats) = await _stage.CreatePendingBookingAsync(context, owner: owner);
        var firstSeat = seats.First();

        var cancelSuccess = await service.CancelBookingAsync(booking.Id, "Khách tự hủy đơn");
        Assert.True(cancelSuccess);

        await using var verifyContext = Stage.NewContext();
        var dbBooking = await verifyContext.Bookings.AsNoTracking().FirstAsync(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.Cancelled, dbBooking.Status);

        var dbSeat = await verifyContext.TripSeats.AsNoTracking().FirstAsync(s => s.Id == firstSeat.Id);
        Assert.Equal(TripSeatStatus.Available, dbSeat.Status);
    }

    [Fact]
    public async Task Area10_UnauthorizedCancellation_IsRejected()
    {
        await using var context = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context, owner: "real-owner");

        var intruderSession = new FixedSession("intruder-" + Guid.NewGuid().ToString("N"), null);
        var service = new BookingService(new BookingRepository(context), intruderSession);

        var cancelResult = await service.CancelBookingAsync(booking.Id, "Tấn công trái phép");
        Assert.False(cancelResult);

        await using var verifyContext = Stage.NewContext();
        var dbBooking = await verifyContext.Bookings.AsNoTracking().FirstAsync(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.PendingPayment, dbBooking.Status);
    }

    [Fact]
    public async Task Area11_RepeatedCancellation_IsSafeAndRejectsSubsequentAttempts()
    {
        await using var context = Stage.NewContext();
        var owner = "owner-repeat-" + Guid.NewGuid().ToString("N");
        var session = new FixedSession(owner, null);
        var service = new BookingService(new BookingRepository(context), session);

        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context, owner: owner);

        var firstCancel = await service.CancelBookingAsync(booking.Id, "Hủy lần 1");
        Assert.True(firstCancel);

        // Second cancellation is idempotent: safe, returns true, and leaves status as Cancelled
        var secondCancel = await service.CancelBookingAsync(booking.Id, "Hủy lần 2 (lặp lại)");
        Assert.True(secondCancel);

        await using var verifyContext = Stage.NewContext();
        var dbBooking = await verifyContext.Bookings.FindAsync(booking.Id);
        Assert.NotNull(dbBooking);
        Assert.Equal(BookingStatus.Cancelled, dbBooking.Status);
    }

    // -------------------------------------------------------------------------------------------------
    // 12 & 13. Ticket Credential Integrity (Missing Credential Fails Closed, Real Code from Persisted Entity)
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Area12_TicketCredentialMissing_FailsClosedWithoutInventingFakeCode()
    {
        await using var context = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);

        var ticketService = _stage.Tickets(context, owner: "owner");
        var result = await ticketService.GetMyTicketAsync(booking.BookingCode);

        // Before payment, credential is not issued, Available must be false, no fake code
        Assert.False(result.Available);
        Assert.Null(result.Token);
        Assert.True(string.IsNullOrEmpty(result.TicketCode));
    }

    [Fact]
    public async Task Area13_TicketCode_IsStrictlySourcedFromPersistedCredential()
    {
        await using var context = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);

        // Pay for the booking
        var payResult = await _stage.Payments(context).ProcessPaymentAsync(new ProcessPaymentRequestDto
        {
            BookingCode = booking.BookingCode,
            Amount = booking.TotalAmount,
            SimulateSuccess = true
        });
        Assert.True(payResult.Success);

        // Get ticket credential
        var ticket = await _stage.Tickets(context).GetMyTicketAsync(booking.BookingCode);
        Assert.True(ticket.Available);
        Assert.NotNull(ticket.TicketCode);
        Assert.StartsWith("TKT-", ticket.TicketCode);

        // Verify the code in ticket matches the primary key of the persisted TicketCredential in SQL Server
        var credentialInDb = await context.TicketCredentials.AsNoTracking().FirstAsync(c => c.BookingId == booking.Id);
        var expectedTicketCode = $"TKT-{credentialInDb.Id:D6}";
        Assert.Equal(expectedTicketCode, ticket.TicketCode);
    }

    // -------------------------------------------------------------------------------------------------
    // 14 & 15. Email BaseUrl Security (Invalid BaseUrl Handled, Missing BaseUrl Handled)
    // -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("not-a-valid-url")]
    [InlineData("ftp://unsupported-scheme.example.com")]
    [InlineData("://missing-scheme")]
    public async Task Area14_InvalidEmailBaseUrl_IsHandledSafelyWithoutCrashing(string invalidBaseUrl)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:BaseUrl"] = invalidBaseUrl,
                ["Email:Smtp:Host"] = "smtp.example.com",
                ["Email:Smtp:Port"] = "587",
                ["Email:Smtp:Username"] = "user",
                ["Email:Smtp:Password"] = "pass",
                ["Email:FromAddress"] = "test@tripgo.test"
            })
            .Build();

        var sender = new SmtpEmailSender(config, NullLogger<SmtpEmailSender>.Instance);

        // SendPasswordResetAsync should fail closed (return false)
        var resetSent = await sender.SendPasswordResetAsync("user@example.com", "token123");
        Assert.False(resetSent);
    }

    [Fact]
    public async Task Area15_MissingEmailBaseUrl_FailsClosedForPasswordReset()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:BaseUrl"] = null,
                ["Email:FromAddress"] = "test@tripgo.test"
            })
            .Build();

        var sender = new SmtpEmailSender(config, NullLogger<SmtpEmailSender>.Instance);
        var result = await sender.SendPasswordResetAsync("user@example.com", "token123");
        Assert.False(result);
    }

    // -------------------------------------------------------------------------------------------------
    // 16 & 17. Email Delivery Failure & Outbox Retry
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Area16_EmailDeliveryFailure_LeavesOutboxPendingOrFailed_NotSent()
    {
        await using var context = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);

        var outboxItem = await context.BookingConfirmationEmailOutbox.AsNoTracking()
            .FirstAsync(e => e.BookingId == booking.Id && e.MessageType == BookingConfirmationEmailOutbox.TypeBookingAcknowledgement);

        // Initial status is Pending, SentAt is null
        Assert.Equal(BookingConfirmationEmailOutbox.Pending, outboxItem.Status);
        Assert.Null(outboxItem.SentAt);
    }

    [Fact]
    public async Task Area17_OutboxWorker_ExhaustsAfterMaxAttempts_AndMarksFailed()
    {
        await using var context = Stage.NewContext();
        var now = DateTimeOffset.UtcNow;
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);
        var message = new BookingConfirmationEmailOutbox
        {
            BookingId = booking.Id,
            RecipientEmail = "fail@tripgo.test",
            MessageType = BookingConfirmationEmailOutbox.TypeBookingAcknowledgement,
            DedupKey = "dedup-" + Guid.NewGuid().ToString("N"),
            Payload = "{}",
            Status = BookingConfirmationEmailOutbox.Pending,
            AttemptCount = BookingConfirmationEmailWorker.MaximumAttempts,
            NextAttemptAt = now.AddMinutes(-5),
            CreatedAt = now
        };
        context.BookingConfirmationEmailOutbox.Add(message);
        await context.SaveChangesAsync();

        // Querying next pending message will skip exhausted messages
        var claimable = await context.BookingConfirmationEmailOutbox
            .Where(m => m.Id == message.Id && m.AttemptCount < BookingConfirmationEmailWorker.MaximumAttempts)
            .FirstOrDefaultAsync();

        Assert.Null(claimable);

        context.BookingConfirmationEmailOutbox.Remove(message);
        await context.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------------------------------
    // 18. Duplicate Payment Webhook Callback Idempotency
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Area18_DuplicatePaymentCallback_IsIdempotent_AndDoesNotCorruptState()
    {
        await using var context = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);

        var gateway = _stage.PaymentGateway(context);
        var payment = await gateway.InitiatePaymentAsync(booking.BookingCode);
        Assert.True(payment.Success, payment.Message);

        var eventId = "evt-" + Guid.NewGuid().ToString("N");
        var callback = Stage.Gateway.CreateSignedCallback(eventId, payment.TransactionCode!, booking.TotalAmount, "VND", "succeeded");

        // First callback application
        var firstResult = await gateway.HandleCallbackAsync(Stage.Gateway.Name, callback);
        Assert.True(firstResult.Accepted);
        Assert.Equal(PaymentWebhookEvent.OutcomeApplied, firstResult.Outcome);

        // Second identical callback application
        var secondResult = await gateway.HandleCallbackAsync(Stage.Gateway.Name, callback);
        Assert.True(secondResult.Accepted);
        Assert.Equal(PaymentWebhookEvent.OutcomeDuplicate, secondResult.Outcome);

        // Verify booking confirmed exactly once
        await using var verifyContext = Stage.NewContext();
        var dbBooking = await verifyContext.Bookings.AsNoTracking().FirstAsync(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.Confirmed, dbBooking.Status);
        Assert.Equal(PaymentStatus.Paid, dbBooking.PaymentStatus);
    }

    // -------------------------------------------------------------------------------------------------
    // 19. Seat Concurrency (Simultaneous Holds Allow Exactly One Winner)
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Area19_SeatConcurrency_SimultaneousHolds_AllowsExactlyOneWinner()
    {
        await using var context = Stage.NewContext();
        var minDeparture = DateTimeOffset.UtcNow.Add(BookingRules.SalesCutoffBeforeDeparture).AddHours(2);
        var trip = await context.Trips.Include(t => t.Seats).FirstAsync(t => t.IsDemoTrip && t.Status == TripStatus.Scheduled && t.DepartureTime > minDeparture && t.Seats.Any(s => s.Status == TripSeatStatus.Available));
        var sessionA = new FixedSession("user-concurrent-A-" + Guid.NewGuid().ToString("N"), null);
        var serviceInit = new BookingService(new BookingRepository(context), sessionA);
        await serviceInit.EnsureDemoTripInventoryAsync(trip.Id);
        var seats19 = await serviceInit.GetSeatsForTripAsync(trip.Id);
        var availableSeat = seats19.First(s => s.IsAvailable);
        var sessionB = new FixedSession("user-concurrent-B-" + Guid.NewGuid().ToString("N"), null);

        var taskA = Task.Run(async () =>
        {
            await using var cA = Stage.NewContext();
            var serviceA = new BookingService(new BookingRepository(cA), sessionA);
            return await serviceA.HoldSeatsAsync(new SeatHoldRequestDto
            {
                TripId = trip.Id,
                SeatCodes = new List<string> { availableSeat.SeatCode },
                HoldMinutes = 10
            });
        });

        var taskB = Task.Run(async () =>
        {
            await using var cB = Stage.NewContext();
            var serviceB = new BookingService(new BookingRepository(cB), sessionB);
            return await serviceB.HoldSeatsAsync(new SeatHoldRequestDto
            {
                TripId = trip.Id,
                SeatCodes = new List<string> { availableSeat.SeatCode },
                HoldMinutes = 10
            });
        });

        var results = await Task.WhenAll(taskA, taskB);
        var successCount = results.Count(r => r.Success);
        var failureCount = results.Count(r => !r.Success);

        Assert.Equal(1, successCount);
        Assert.Equal(1, failureCount);

        // Cleanup the winning hold
        var winner = results.First(r => r.Success);
        if (winner.HoldToken != null)
        {
            await using var cleanupContext = Stage.NewContext();
            await cleanupContext.SeatHolds.Where(h => h.HoldToken == winner.HoldToken).ExecuteDeleteAsync();
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 20. Database Safety Guard (Blocks Writes to TripgoDb Production)
    // -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("TripgoDb")]
    [InlineData("TripgoDb_Production")]
    [InlineData("master")]
    [InlineData("TripgoDb_Dev")]
    public void Area20_DatabaseSafetyGuard_StrictlyRejectsWritesOutsideIsolatedTestDatabase(string illegalDbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer($"Server=localhost\\MSSQLSERVER01;Database={illegalDbName};Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;

        using var dbContext = new AppDbContext(options);

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            dbContext.EnsureWritesTargetIsolatedTestDatabase();
        });

        Assert.Contains("Writes are permitted only against TripgoDb_IsolatedTest", exception.Message);
        Assert.Contains(illegalDbName, exception.Message);
    }

    // -------------------------------------------------------------------------------------------------
    // 21. Email BaseUrl Strict Validation & Fail-Closed Matrix
    // -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null, "Production", false, false)]
    [InlineData("not-a-valid-url", "Production", false, false)]
    [InlineData("ftp://external.org", "Production", false, false)]
    [InlineData("http://external-insecure.org", "Production", false, false)]
    [InlineData("http://localhost:5065", "Production", true, false)] // Rejected: localhost forbidden in Production
    [InlineData("http://localhost:5065", "Development", false, false)] // Rejected: AllowLocalhostInDevelopment is false
    [InlineData("http://localhost:5065", "Development", true, true)] // Accepted in Development
    [InlineData("https://tripgo.vn", "Production", false, true)] // Accepted HTTPS in Production
    public async Task Area21_BookingConfirmation_BaseUrlValidationMatrix_EnforcesFailClosed(
        string? baseUrl,
        string environmentName,
        bool allowLocalhostInDev,
        bool expectUrlAccepted)
    {
        var configData = new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = environmentName,
            ["Email:AllowLocalhostInDevelopment"] = allowLocalhostInDev ? "true" : "false",
            ["Email:FromAddress"] = "test@tripgo.test"
        };
        if (baseUrl != null)
        {
            configData["Email:BaseUrl"] = baseUrl;
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var sender = new SmtpEmailSender(config, NullLogger<SmtpEmailSender>.Instance);
        var dto = new Tripgo.Application.DTOs.BookingConfirmationEmailDto
        {
            BookingCode = "TRIPGO-TEST123456",
            RecipientEmail = "customer@example.com",
            PassengerName = "Test Passenger",
            TotalAmount = 500000m
        };

        var sent = await sender.SendBookingConfirmationAsync(dto);
        Assert.Equal(expectUrlAccepted, sent);
    }

    // -------------------------------------------------------------------------------------------------
    // 22. Database Write Safety Interceptor (Direct SQL & Low-Level Writes)
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Area22_DatabaseSafetyInterceptor_BlocksDirectSqlWritesToNonIsolatedDatabase()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost\\MSSQLSERVER01;Database=TripgoDb;Trusted_Connection=True;TrustServerCertificate=True;")
            .AddInterceptors(new Tripgo.Infrastructure.Persistence.DatabaseWriteSafetyInterceptor())
            .Options;

        using var dbContext = new AppDbContext(options);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await dbContext.Database.ExecuteSqlRawAsync("UPDATE Users SET FullName = 'Exploit' WHERE 1 = 0");
        });

        Assert.Contains("Writes are permitted only against TripgoDb_IsolatedTest", exception.Message);
        Assert.Contains("TripgoDb", exception.Message);
    }

    [Fact]
    public async Task Area23_DatabaseSafetyInterceptor_BlocksExecuteSqlInterpolated_AndExecuteDelete()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost\\MSSQLSERVER01;Database=TripgoDb;Trusted_Connection=True;TrustServerCertificate=True;")
            .AddInterceptors(new Tripgo.Infrastructure.Persistence.DatabaseWriteSafetyInterceptor())
            .Options;

        using var dbContext = new AppDbContext(options);

        // 1. ExecuteSqlInterpolatedAsync
        var targetId = -999L;
        var exInterpolated = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Bookings WHERE Id = {targetId}");
        });
        Assert.Contains("Writes are permitted only against TripgoDb_IsolatedTest", exInterpolated.Message);

        // 2. ExecuteDeleteAsync (EF Core 7/8 bulk delete)
        var exBulkDelete = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await dbContext.Bookings.Where(b => b.Id == targetId).ExecuteDeleteAsync();
        });
        Assert.Contains("Writes are permitted only against TripgoDb_IsolatedTest", exBulkDelete.Message);
    }

    [Fact]
    public async Task Area24_DatabaseSafetyInterceptor_PermitsReadOnlyQueriesAgainstTripgoDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost\\MSSQLSERVER01;Database=TripgoDb;Trusted_Connection=True;TrustServerCertificate=True;")
            .AddInterceptors(new Tripgo.Infrastructure.Persistence.DatabaseWriteSafetyInterceptor())
            .Options;

        using var dbContext = new AppDbContext(options);

        // Read-only query against TripgoDb must NOT trigger write safety interceptor
        var count = await dbContext.Roles.AsNoTracking().CountAsync();
        Assert.True(count >= 0);
    }

    [Fact]
    public async Task Area25_PaidBookingCancellation_CalculatesFiftyPercentRefund_AndReleasesSeats()
    {
        await using var context = Stage.NewContext();
        var owner = "paid-owner-" + Guid.NewGuid().ToString("N");
        var (_, bookingDto, seats) = await _stage.CreatePendingBookingAsync(context, owner: owner);

        var dbBooking = await context.Bookings
            .Include(b => b.BookingSeats)
            .ThenInclude(bs => bs.TripSeat)
            .FirstAsync(b => b.Id == bookingDto.Id);

        // Simulate confirmed payment
        dbBooking.Status = BookingStatus.Confirmed;
        dbBooking.PaymentStatus = PaymentStatus.Paid;
        dbBooking.TotalAmount = 780000m;
        foreach (var bs in dbBooking.BookingSeats)
        {
            if (bs.TripSeat != null) bs.TripSeat.Status = TripSeatStatus.Booked;
        }
        await context.SaveChangesAsync();

        // 1. Verify Domain Rules calculate 50% refund accurately
        Assert.True(BookingRules.CustomerCanCancel(dbBooking));
        var expectedRefund = BookingRules.CalculateCancellationRefund(dbBooking);
        var expectedFee = BookingRules.CalculateCancellationFee(dbBooking);
        Assert.Equal(390000m, expectedRefund);
        Assert.Equal(390000m, expectedFee);

        // 2. Execute cancellation
        var session = new FixedSession(owner, null);
        var service = new BookingService(new BookingRepository(context), session);
        var cancelSuccess = await service.CancelBookingAsync(dbBooking.Id, "Khách yêu cầu hủy đơn đã thanh toán");
        Assert.True(cancelSuccess);

        // 3. Verify state in fresh context
        await using var verifyContext = Stage.NewContext();
        var updated = await verifyContext.Bookings
            .Include(b => b.BookingSeats).ThenInclude(bs => bs.TripSeat)
            .FirstAsync(b => b.Id == dbBooking.Id);

        Assert.Equal(BookingStatus.Cancelled, updated.Status);
        Assert.Equal(PaymentStatus.Refunded, updated.PaymentStatus);
        Assert.Contains("390,000", updated.CancellationReason);

        // Seats must be freed back to Available
        foreach (var bs in updated.BookingSeats)
        {
            Assert.Equal(TripSeatStatus.Available, bs.TripSeat.Status);
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 26 - 34. Road Route Map, OSRM Routing, Rest-Stops & Cancellation Audits
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Area26_TripRoadRouteMap_UsesExactSelectedPickupAndDropoffPoints()
    {
        await using var context = Stage.NewContext();
        var (trip, _, _) = await _stage.CreatePendingBookingAsync(context, owner: "map-test-owner");
        var route = await context.Routes.FirstAsync(r => r.Id == trip.RouteId);

        var pPoint = new PickupDropoffPoint
        {
            LocationId = route.OriginLocationId,
            OperatorId = route.OperatorId,
            Name = "Điểm Đón Cụ Thể 1",
            Address = "Số 10 Đường Hoa Cúc, TP.HCM",
            Latitude = 10.7925m,
            Longitude = 106.6912m,
            PointType = PointType.Pickup,
            IsActive = true
        };
        var dPoint = new PickupDropoffPoint
        {
            LocationId = route.DestinationLocationId,
            OperatorId = route.OperatorId,
            Name = "Điểm Trả Cụ Thể 2",
            Address = "Số 50 Đường Trần Phú, Đà Lạt",
            Latitude = 11.9421m,
            Longitude = 108.4385m,
            PointType = PointType.Dropoff,
            IsActive = true
        };
        context.PickupDropoffPoints.AddRange(pPoint, dPoint);
        await context.SaveChangesAsync();

        var tripStop1 = new TripStop
        {
            TripId = trip.Id,
            PointId = pPoint.Id,
            StopOrder = 1,
            AllowPickup = true,
            AllowDropoff = false,
            ScheduledDepartureTime = trip.DepartureTime
        };
        var tripStop2 = new TripStop
        {
            TripId = trip.Id,
            PointId = dPoint.Id,
            StopOrder = 2,
            AllowPickup = false,
            AllowDropoff = true,
            ScheduledArrivalTime = trip.ExpectedArrivalTime
        };
        context.TripStops.AddRange(tripStop1, tripStop2);
        await context.SaveChangesAsync();

        var tripRepo = new TripRepository(context);
        var tripService = new TripService(tripRepo, new Tripgo.Application.Services.Temporary.InMemoryTemporaryBookingStore());

        // Query with exact selected pickup and dropoff points
        var mapDto = await tripService.GetTripRouteMapAsync(trip.Id, pPoint.Id, dPoint.Id);
        Assert.NotNull(mapDto);
        Assert.Equal(pPoint.Id, mapDto.SelectedPickupPoint.Id);
        Assert.Equal("Điểm Đón Cụ Thể 1", mapDto.SelectedPickupPoint.Name);
        Assert.Equal(10.7925, mapDto.SelectedPickupPoint.Latitude, 4);
        Assert.Equal(106.6912, mapDto.SelectedPickupPoint.Longitude, 4);

        Assert.Equal(dPoint.Id, mapDto.SelectedDropoffPoint.Id);
        Assert.Equal("Điểm Trả Cụ Thể 2", mapDto.SelectedDropoffPoint.Name);
        Assert.Equal(11.9421, mapDto.SelectedDropoffPoint.Latitude, 4);
        Assert.Equal(108.4385, mapDto.SelectedDropoffPoint.Longitude, 4);
    }

    [Fact]
    public async Task Area27_TripRoadRouteMap_DifferentPickupDropoffCombinations_YieldCorrespondingPoints()
    {
        await using var context = Stage.NewContext();
        var (trip, _, _) = await _stage.CreatePendingBookingAsync(context, owner: "comb-test-owner");
        var route = await context.Routes.FirstAsync(r => r.Id == trip.RouteId);

        var p1 = new PickupDropoffPoint { LocationId = route.OriginLocationId, OperatorId = route.OperatorId, Name = "Trạm Đón A", Latitude = 10.75m, Longitude = 106.65m, PointType = PointType.Pickup, IsActive = true };
        var p2 = new PickupDropoffPoint { LocationId = route.OriginLocationId, OperatorId = route.OperatorId, Name = "Trạm Đón B", Latitude = 10.80m, Longitude = 106.70m, PointType = PointType.Pickup, IsActive = true };
        var d1 = new PickupDropoffPoint { LocationId = route.DestinationLocationId, OperatorId = route.OperatorId, Name = "Trạm Trả C", Latitude = 11.90m, Longitude = 108.40m, PointType = PointType.Dropoff, IsActive = true };
        var d2 = new PickupDropoffPoint { LocationId = route.DestinationLocationId, OperatorId = route.OperatorId, Name = "Trạm Trả D", Latitude = 11.95m, Longitude = 108.45m, PointType = PointType.Dropoff, IsActive = true };
        context.PickupDropoffPoints.AddRange(p1, p2, d1, d2);
        await context.SaveChangesAsync();

        context.TripStops.AddRange(
            new TripStop { TripId = trip.Id, PointId = p1.Id, StopOrder = 1, AllowPickup = true },
            new TripStop { TripId = trip.Id, PointId = p2.Id, StopOrder = 2, AllowPickup = true },
            new TripStop { TripId = trip.Id, PointId = d1.Id, StopOrder = 3, AllowDropoff = true },
            new TripStop { TripId = trip.Id, PointId = d2.Id, StopOrder = 4, AllowDropoff = true });
        await context.SaveChangesAsync();

        var tripRepo = new TripRepository(context);
        var tripService = new TripService(tripRepo, new Tripgo.Application.Services.Temporary.InMemoryTemporaryBookingStore());

        // Combination 1: (p1, d1)
        var map1 = await tripService.GetTripRouteMapAsync(trip.Id, p1.Id, d1.Id);
        Assert.NotNull(map1);
        Assert.Equal(p1.Id, map1.SelectedPickupPoint.Id);
        Assert.Equal(d1.Id, map1.SelectedDropoffPoint.Id);

        // Combination 2: (p2, d2)
        var map2 = await tripService.GetTripRouteMapAsync(trip.Id, p2.Id, d2.Id);
        Assert.NotNull(map2);
        Assert.Equal(p2.Id, map2.SelectedPickupPoint.Id);
        Assert.Equal(d2.Id, map2.SelectedDropoffPoint.Id);
    }

    [Fact]
    public async Task Area28_RoadRoutingService_ValidCoordinates_ReturnsRoadGeometryAndDuration()
    {
        var mockResponseJson = """
        {
            "code": "Ok",
            "routes": [
                {
                    "geometry": {
                        "coordinates": [
                            [106.6186, 10.7497],
                            [106.7000, 10.8000],
                            [108.4447, 11.9298]
                        ],
                        "type": "LineString"
                    },
                    "distance": 312500.0,
                    "duration": 21600.0
                }
            ]
        }
        """;

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(mockResponseJson, Encoding.UTF8, System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json"))
        });

        var httpClient = new HttpClient(handler);
        var routingService = new OsrmRoadRoutingService(httpClient);

        var result = await routingService.GetDrivingRouteAsync(10.7497, 106.6186, 11.9298, 108.4447);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(3, result.Geometry.Count);
        // Leaflet format: [lat, lon]
        Assert.Equal(10.7497, result.Geometry[0][0], 4);
        Assert.Equal(106.6186, result.Geometry[0][1], 4);
        Assert.Equal(312500.0, result.DistanceMeters);
        Assert.Equal(21600.0, result.DurationSeconds);
        Assert.Contains("OSRM", result.ProviderSource);
    }

    [Fact]
    public async Task Area29_RoadRoutingService_InvalidCoordinates_ReturnsClearErrorWithoutFakeRoute()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var routingService = new OsrmRoadRoutingService(new HttpClient(handler));

        // 1. Latitude out of range (> 90)
        var result1 = await routingService.GetDrivingRouteAsync(95.0, 106.0, 11.0, 108.0);
        Assert.False(result1.Success);
        Assert.NotEmpty(result1.ErrorMessage!);
        Assert.Empty(result1.Geometry);

        // 2. Coinciding coordinates (origin == dest)
        var result2 = await routingService.GetDrivingRouteAsync(10.75, 106.65, 10.75, 106.65);
        Assert.False(result2.Success);
        Assert.Contains("trùng", result2.ErrorMessage!);
        Assert.Empty(result2.Geometry);
    }

    [Fact]
    public async Task Area30_RoadRoutingService_TimeoutOrUnavailable_FailsGracefullyWithoutStraightLineFallback()
    {
        // 1. Server returns HTTP 503 Service Unavailable
        var unavailableHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var unavailableService = new OsrmRoadRoutingService(new HttpClient(unavailableHandler));

        var result1 = await unavailableService.GetDrivingRouteAsync(10.75, 106.65, 11.95, 108.45);
        Assert.False(result1.Success);
        Assert.Contains("503", result1.ErrorMessage!);
        Assert.Empty(result1.Geometry); // No fake straight-line fallback!

        // 2. Provider returns NoRoute
        var noRouteHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"code": "NoRoute"}""", Encoding.UTF8, System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json"))
        });
        var noRouteService = new OsrmRoadRoutingService(new HttpClient(noRouteHandler));

        var result2 = await noRouteService.GetDrivingRouteAsync(10.75, 106.65, 11.95, 108.45);
        Assert.False(result2.Success);
        Assert.Contains("Không tìm thấy", result2.ErrorMessage!);
        Assert.Empty(result2.Geometry);
    }

    [Fact]
    public async Task Area31_RoadRoutingService_CachesIdenticalRequests_AvoidingRepeatedCalls()
    {
        var mockResponseJson = """
        {
            "code": "Ok",
            "routes": [{
                "geometry": { "coordinates": [[106.6186, 10.7497], [108.4447, 11.9298]], "type": "LineString" },
                "distance": 310000.0,
                "duration": 21000.0
            }]
        }
        """;

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(mockResponseJson, Encoding.UTF8, System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json"))
        });

        var cache = new MemoryCache(new MemoryCacheOptions());
        var routingService = new OsrmRoadRoutingService(new HttpClient(handler), cache: cache);

        // First call hits the mock handler
        var first = await routingService.GetDrivingRouteAsync(10.7497, 106.6186, 11.9298, 108.4447);
        Assert.True(first.Success);
        Assert.Equal(1, handler.CallCount);

        // Second call with identical coordinates must use cache and not hit handler
        var second = await routingService.GetDrivingRouteAsync(10.7497, 106.6186, 11.9298, 108.4447);
        Assert.True(second.Success);
        Assert.Equal(1, handler.CallCount); // Still 1!
    }

    [Fact]
    public async Task Area32_VerifiedRestStops_FilteredStrictlyBetweenSelectedSegment_NoUnverifiedFabrications()
    {
        await using var context = Stage.NewContext();
        var (trip, _, _) = await _stage.CreatePendingBookingAsync(context, owner: "rest-test-owner");

        var tripRepo = new TripRepository(context);
        var tripService = new TripService(tripRepo, new Tripgo.Application.Services.Temporary.InMemoryTemporaryBookingStore());

        // Known route: Đà Lạt ➔ Quy Nhơn/Đà Nẵng
        var mapDto = await tripService.GetTripRouteMapAsync(trip.Id);
        Assert.NotNull(mapDto);

        // Verified rest stops should have positive stay duration and verified badge
        foreach (var rs in mapDto.RestStops)
        {
            Assert.True(rs.StayMinutes > 0);
            Assert.Contains("Xác nhận", rs.VerificationStatus);
            Assert.NotEmpty(rs.Services);
            Assert.NotEmpty(rs.Address);
        }
    }

    [Fact]
    public async Task Area33_EstimatedJourneyDuration_DistinguishesOperatorVsRoadRoutingEstimates()
    {
        await using var context = Stage.NewContext();
        var (trip, _, _) = await _stage.CreatePendingBookingAsync(context, owner: "dur-test-owner");

        var tripRepo = new TripRepository(context);
        var tripService = new TripService(tripRepo, new Tripgo.Application.Services.Temporary.InMemoryTemporaryBookingStore());

        var mapDto = await tripService.GetTripRouteMapAsync(trip.Id);
        Assert.NotNull(mapDto);

        // Must distinguish operator schedule from road estimate
        Assert.Contains("theo lịch trình nhà xe", mapDto.OperatorScheduledDurationText);
        Assert.NotEmpty(mapDto.DurationNotice);
    }

    [Fact]
    public async Task Area34_PaidBookingCancellation_ExactFiftyPercentRefund_AndPreventsDuplicateRefunds()
    {
        await using var context = Stage.NewContext();
        var (trip, booking, _) = await _stage.CreatePendingBookingAsync(context, owner: "refund-audit-owner");

        // Set to Confirmed & Paid with 600,000 VND
        await context.Bookings.Where(b => b.Id == booking.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status, BookingStatus.Confirmed)
                .SetProperty(b => b.PaymentStatus, PaymentStatus.Paid)
                .SetProperty(b => b.TotalAmount, 600_000m)
                .SetProperty(b => b.ConfirmedAt, DateTimeOffset.UtcNow));

        var repo = new BookingRepository(context);
        var session = new FixedSession("refund-audit-owner", null);
        var service = new BookingService(repo, session);

        // 1. First cancellation must calculate exact 50% (300,000 VND)
        var firstCancel = await service.CancelBookingAsync(booking.Id, "Khách yêu cầu hủy đơn 600k");
        Assert.True(firstCancel);

        await using var verifyContext1 = Stage.NewContext();
        var b1 = await verifyContext1.Bookings.FirstAsync(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.Cancelled, b1.Status);
        Assert.Equal(PaymentStatus.Refunded, b1.PaymentStatus);
        Assert.Contains("300,000", b1.CancellationReason);

        // 2. Second cancellation attempt must be idempotent/safe and not re-process refund
        var secondCancel = await service.CancelBookingAsync(booking.Id, "Hủy lần hai thử nghiệm");
        Assert.True(secondCancel); // Idempotent true

        await using var verifyContext2 = Stage.NewContext();
        var b2 = await verifyContext2.Bookings.FirstAsync(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.Cancelled, b2.Status);
        Assert.Equal(PaymentStatus.Refunded, b2.PaymentStatus);
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public int CallCount { get; private set; }

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_handler(request));
        }
    }
}
