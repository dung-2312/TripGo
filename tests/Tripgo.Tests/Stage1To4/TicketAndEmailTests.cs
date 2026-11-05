using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tripgo.Application.DTOs;
using Tripgo.Application.Interfaces;
using Tripgo.Application.Services;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;
using Tripgo.Infrastructure.Persistence;
using Tripgo.Infrastructure.Services;
using Tripgo.Web.Services;

namespace Tripgo.Tests.Stage1To4;

[Collection("Database")]
public sealed class TicketAndEmailTests : IAsyncLifetime
{
    private readonly Stage _stage = new();

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => _stage.CleanupAsync();

    private async Task<(AppDbContext Context, string Token, long BookingId, string Code, long StaffId)> PaidTicketAsync()
    {
        var context = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);
        Assert.True((await _stage.Payments(context).ProcessPaymentAsync(new ProcessPaymentRequestDto
        {
            BookingCode = booking.BookingCode,
            Amount = booking.TotalAmount,
            SimulateSuccess = true
        })).Success);
        var ticket = await _stage.Tickets(context).GetMyTicketAsync(booking.BookingCode);
        Assert.True(ticket.Available, ticket.Message);
        var staff = await _stage.CreateUserAsync(context, RoleNames.Admin);
        return (context, ticket.Token!, booking.Id, booking.BookingCode, staff);
    }

    // ---------------------------------------------------------------- tickets

    [Fact]
    public async Task Ticket_IsNotIssued_BeforePayment()
    {
        await using var context = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);

        var ticket = await _stage.Tickets(context).GetMyTicketAsync(booking.BookingCode);

        Assert.False(ticket.Available);
        Assert.Null(ticket.Token);
        Assert.False(await context.TicketCredentials.AnyAsync(c => c.BookingId == booking.Id));
    }

    [Fact]
    public async Task Ticket_IsOnlyVisibleToItsOwner()
    {
        var (context, _, _, code, _) = await PaidTicketAsync();
        await using var _ = context;

        var other = await _stage.Tickets(context, owner: Guid.NewGuid().ToString("N")).GetMyTicketAsync(code);

        Assert.False(other.Available);
        Assert.Null(other.Token);
    }

    [Fact]
    public async Task Ticket_IsSandboxFlagged_AndItsTokenCarriesNoPersonalData()
    {
        var (context, token, bookingId, code, _) = await PaidTicketAsync();
        await using var _ = context;

        var ticket = await _stage.Tickets(context).GetMyTicketAsync(code);
        Assert.True(ticket.IsSandbox);

        foreach (var personal in new[] { "Nguyen", "0912345678", _stage.Marker, code, bookingId.ToString() })
        {
            Assert.DoesNotContain(personal, token, StringComparison.OrdinalIgnoreCase);
        }

        // Only a hash is searchable; the raw token is not stored anywhere.
        var stored = await context.TicketCredentials.AsNoTracking().SingleAsync(c => c.BookingId == bookingId);
        Assert.NotEqual(token, stored.TokenHash);
        Assert.Equal(TicketService.HashToken(token), stored.TokenHash);
    }

    [Fact]
    public async Task Ticket_RepeatedRequests_ReuseOneCredential()
    {
        var (context, token, bookingId, code, _) = await PaidTicketAsync();
        await using var _ = context;

        var again = await _stage.Tickets(context).GetMyTicketAsync(code);

        Assert.Equal(token, again.Token);
        Assert.Equal(1, await context.TicketCredentials.CountAsync(c => c.BookingId == bookingId));
    }

    [Fact]
    public async Task Verify_RequiresStaff()
    {
        await using var context = Stage.NewContext();
        var customer = await _stage.CreateUserAsync(context, RoleNames.Customer);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _stage.Tickets(context, userId: customer).VerifyAsync("tgt_x"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _stage.Tickets(context).BoardAsync("tgt_x"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("tgt_0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task InvalidToken_IsRejected_AndAudited(string token)
    {
        await using var context = Stage.NewContext();
        var staff = await _stage.CreateUserAsync(context, RoleNames.Admin);

        var result = await _stage.Tickets(context, userId: staff).VerifyAsync(token);

        Assert.Equal(TicketVerificationResult.InvalidToken, result.Result);
        Assert.Null(result.BookingCode);
        Assert.True(await context.TicketVerificationAttempts.AnyAsync(a => a.ActorUserId == staff && a.Result == nameof(TicketVerificationResult.InvalidToken)));
        await context.TicketVerificationAttempts.Where(a => a.ActorUserId == staff).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Verify_DoesNotConsumeTheTicket_AndShowsNoContactDetails()
    {
        var (context, token, bookingId, _, staff) = await PaidTicketAsync();
        await using var _ = context;
        var service = _stage.Tickets(context, userId: staff);

        var first = await service.VerifyAsync(token);
        var second = await service.VerifyAsync(token);

        Assert.Equal(TicketVerificationResult.Valid, first.Result);
        Assert.Equal(TicketVerificationResult.Valid, second.Result);
        Assert.True(first.IsSandbox);
        Assert.DoesNotContain("Nguyen", first.MaskedPassengerName);
        Assert.Null(await context.TicketCredentials.Where(c => c.BookingId == bookingId).Select(c => c.BoardedAt).SingleAsync());
    }

    [Fact]
    public async Task ReusedToken_IsRejected_AfterBoarding()
    {
        var (context, token, _, _, staff) = await PaidTicketAsync();
        await using var _ = context;
        var service = _stage.Tickets(context, userId: staff);

        var first = await service.BoardAsync(token);
        var second = await service.BoardAsync(token);

        Assert.Equal(TicketVerificationResult.Boarded, first.Result);
        Assert.Equal(TicketVerificationResult.AlreadyBoarded, second.Result);
    }

    [Fact]
    public async Task ConcurrentBoarding_SucceedsExactlyOnce()
    {
        var (context, token, _, _, staff) = await PaidTicketAsync();
        await using var _ = context;

        var attempts = Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var attemptContext = Stage.NewContext();
            return await _stage.Tickets(attemptContext, userId: staff).BoardAsync(token);
        });
        var results = await Task.WhenAll(attempts);

        Assert.Single(results, r => r.Result == TicketVerificationResult.Boarded);
        Assert.All(results.Where(r => r.Result != TicketVerificationResult.Boarded),
            r => Assert.Equal(TicketVerificationResult.AlreadyBoarded, r.Result));
    }

    [Fact]
    public async Task RevokedToken_IsRejected()
    {
        var (context, token, bookingId, _, staff) = await PaidTicketAsync();
        await using var _ = context;
        var service = _stage.Tickets(context, userId: staff);

        Assert.True(await service.RevokeAsync(bookingId, "Nghi ngờ gian lận"));

        Assert.Equal(TicketVerificationResult.Revoked, (await service.VerifyAsync(token)).Result);
    }

    [Fact]
    public async Task ExpiredToken_IsRejected()
    {
        var (context, token, bookingId, _, staff) = await PaidTicketAsync();
        await using var _ = context;
        await context.TicketCredentials.Where(c => c.BookingId == bookingId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.Equal(TicketVerificationResult.Expired, (await _stage.Tickets(context, userId: staff).VerifyAsync(token)).Result);
    }

    [Fact]
    public async Task CancelledTrip_InvalidatesItsTickets()
    {
        var (context, token, bookingId, _, staff) = await PaidTicketAsync();
        await using var _ = context;
        var tripId = await context.Bookings.Where(b => b.Id == bookingId).Select(b => b.TripId).SingleAsync();
        await context.Trips.Where(t => t.Id == tripId).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TripStatus.Cancelled));

        Assert.Equal(TicketVerificationResult.TripCancelled, (await _stage.Tickets(context, userId: staff).VerifyAsync(token)).Result);
    }

    [Fact]
    public async Task Operator_CannotCheckAnotherOperatorsTicket_AndLearnsNothingAboutIt()
    {
        var (context, token, _, _, _) = await PaidTicketAsync();
        await using var _ = context;
        var operatorUser = await _stage.CreateUserAsync(context, RoleNames.Operator, operatorId: -1);

        var result = await _stage.Tickets(context, userId: operatorUser).BoardAsync(token);

        Assert.Equal(TicketVerificationResult.WrongOperator, result.Result);
        Assert.Null(result.BookingCode);
        Assert.Empty(result.SeatCodes);
    }

    [Fact]
    public async Task Verification_IsRateLimited_PerStaffAccount()
    {
        await using var context = Stage.NewContext();
        var staff = await _stage.CreateUserAsync(context, RoleNames.Admin);
        var service = _stage.Tickets(context, userId: staff);

        var last = new TicketVerificationDto();
        for (var attempt = 0; attempt <= TicketService.MaxAttemptsPerMinute; attempt++)
        {
            last = await service.VerifyAsync($"guess-{attempt}");
        }

        Assert.Equal(TicketVerificationResult.RateLimited, last.Result);
        await context.TicketVerificationAttempts.Where(a => a.ActorUserId == staff).ExecuteDeleteAsync();
    }

    // ---------------------------------------------------------------- email

    private sealed class CountingSender : IEmailSender
    {
        public List<string> Sent { get; } = new();
        public bool ShouldThrow { get; set; }

        public Task<bool> SendPasswordResetAsync(string email, string token, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> SendBookingConfirmationAsync(BookingConfirmationEmailDto confirmation, CancellationToken cancellationToken = default)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Simulated SMTP failure.");
            }

            Sent.Add($"{confirmation.BookingCode}:{confirmation.MessageType}");
            return Task.FromResult(true);
        }
    }

    private static async Task RunWorkerAsync(IEmailSender sender)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => Stage.NewContext());
        services.AddSingleton(sender);
        await using var provider = services.BuildServiceProvider();
        var worker = new BookingConfirmationEmailWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BookingConfirmationEmailWorker>.Instance);
        for (var pass = 0; pass < 3; pass++)
        {
            await worker.ProcessBatchAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task EveryStageOfABooking_QueuesItsOwnEmail()
    {
        await using var context = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);
        await _stage.Payments(context).ProcessPaymentAsync(new ProcessPaymentRequestDto { BookingCode = booking.BookingCode, Amount = booking.TotalAmount, SimulateSuccess = false });
        await _stage.Bookings(context).CancelBookingAsync(booking.Id, "Khách đổi kế hoạch");

        var types = await context.BookingConfirmationEmailOutbox.Where(e => e.BookingId == booking.Id).Select(e => e.MessageType).ToListAsync();

        Assert.Contains(BookingConfirmationEmailOutbox.TypeBookingAcknowledgement, types);
        Assert.Contains(BookingConfirmationEmailOutbox.TypePaymentFailed, types);
        Assert.Contains(BookingConfirmationEmailOutbox.TypeBookingCancellation, types);
    }

    [Fact]
    public async Task RepeatedFailures_EachNotify_ButARepeatOfOneMessageDoesNot()
    {
        await using var context = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);
        var request = new ProcessPaymentRequestDto { BookingCode = booking.BookingCode, Amount = booking.TotalAmount, SimulateSuccess = false };

        await _stage.Payments(context).ProcessPaymentAsync(request);
        await _stage.Payments(context).ProcessPaymentAsync(request);

        Assert.Equal(2, await context.BookingConfirmationEmailOutbox.CountAsync(e =>
            e.BookingId == booking.Id && e.MessageType == BookingConfirmationEmailOutbox.TypePaymentFailed));

        var duplicate = await context.BookingConfirmationEmailOutbox.AsNoTracking().FirstAsync(e =>
            e.BookingId == booking.Id && e.MessageType == BookingConfirmationEmailOutbox.TypePaymentFailed);
        duplicate.Id = 0;
        context.BookingConfirmationEmailOutbox.Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task OutboxSendFailure_IsRetried_ThenSucceeds()
    {
        await using var context = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);
        var sender = new CountingSender { ShouldThrow = true };

        await RunWorkerAsync(sender);

        var failed = await context.BookingConfirmationEmailOutbox.AsNoTracking().FirstAsync(e => e.BookingId == booking.Id);
        Assert.Equal(BookingConfirmationEmailOutbox.Pending, failed.Status);
        Assert.Equal(1, failed.AttemptCount);
        Assert.NotNull(failed.LastError);
        Assert.True(failed.NextAttemptAt > DateTimeOffset.UtcNow);

        await context.BookingConfirmationEmailOutbox.Where(e => e.BookingId == booking.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.NextAttemptAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        sender.ShouldThrow = false;
        await RunWorkerAsync(sender);

        var sent = await context.BookingConfirmationEmailOutbox.AsNoTracking().FirstAsync(e => e.BookingId == booking.Id);
        Assert.Equal(BookingConfirmationEmailOutbox.Sent, sent.Status);
        Assert.Contains(sender.Sent, s => s.StartsWith(booking.BookingCode, StringComparison.Ordinal));
    }

    [Fact]
    public async Task WorkerRestart_RecoversAnAbandonedMessage_AndDoesNotResendACompletedOne()
    {
        await using var context = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);

        // As if a worker claimed this message and then the process died.
        await context.BookingConfirmationEmailOutbox.Where(e => e.BookingId == booking.Id).ExecuteUpdateAsync(s => s
            .SetProperty(e => e.Status, BookingConfirmationEmailOutbox.Processing)
            .SetProperty(e => e.AttemptCount, 1)
            .SetProperty(e => e.LockedUntil, DateTimeOffset.UtcNow.AddMinutes(-1)));

        var sender = new CountingSender();
        await RunWorkerAsync(sender);
        await RunWorkerAsync(sender);

        Assert.Equal(1, sender.Sent.Count(s => s.StartsWith(booking.BookingCode, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task MessageThatDiedOnItsLastAttempt_StopsInsteadOfRetryingForever()
    {
        await using var context = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);
        await context.BookingConfirmationEmailOutbox.Where(e => e.BookingId == booking.Id).ExecuteUpdateAsync(s => s
            .SetProperty(e => e.Status, BookingConfirmationEmailOutbox.Processing)
            .SetProperty(e => e.AttemptCount, 5)
            .SetProperty(e => e.LockedUntil, DateTimeOffset.UtcNow.AddMinutes(-1)));

        var sender = new CountingSender();
        await RunWorkerAsync(sender);

        var stored = await context.BookingConfirmationEmailOutbox.AsNoTracking().FirstAsync(e => e.BookingId == booking.Id);
        Assert.Equal(BookingConfirmationEmailOutbox.Failed, stored.Status);
        Assert.DoesNotContain(sender.Sent, s => s.StartsWith(booking.BookingCode, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AdminRetry_ReopensOnlyFailedMessages()
    {
        await using var context = Stage.NewContext();
        var admin = _stage.Admin(context, await _stage.CreateUserAsync(context, RoleNames.Admin));
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);
        var message = await context.BookingConfirmationEmailOutbox.AsNoTracking().FirstAsync(e => e.BookingId == booking.Id);

        Assert.False((await admin.RetryEmailAsync(message.Id)).Success);

        await context.BookingConfirmationEmailOutbox.Where(e => e.Id == message.Id).ExecuteUpdateAsync(s => s
            .SetProperty(e => e.Status, BookingConfirmationEmailOutbox.Failed)
            .SetProperty(e => e.AttemptCount, 5));

        Assert.True((await admin.RetryEmailAsync(message.Id)).Success);
        var stored = await context.BookingConfirmationEmailOutbox.AsNoTracking().SingleAsync(e => e.Id == message.Id);
        Assert.Equal(BookingConfirmationEmailOutbox.Pending, stored.Status);
        Assert.Equal(0, stored.AttemptCount);
    }

    [Theory]
    [InlineData(BookingConfirmationEmailOutbox.TypeBookingAcknowledgement, "Đã nhận đơn")]
    [InlineData(BookingConfirmationEmailOutbox.TypePaymentFailed, "Thanh toán chưa thành công")]
    [InlineData(BookingConfirmationEmailOutbox.TypeBookingCancellation, "Đơn đặt chỗ đã hủy")]
    [InlineData(BookingConfirmationEmailOutbox.TypeBookingConfirmation, "Thông tin đặt chỗ")]
    public void EmailTemplates_MatchTheirMessageType(string messageType, string expected)
    {
        var template = BookingEmailTemplate.For(messageType, "TRIPGO-TEST", null);

        Assert.Contains(expected, template.Subject);
        Assert.Contains("TRIPGO-TEST", template.Subject);
    }

    [Fact]
    public void CancellationEmail_IncludesTheReason()
    {
        var template = BookingEmailTemplate.For(BookingConfirmationEmailOutbox.TypeBookingCancellation, "X", "Xe hỏng");

        Assert.Contains("Xe hỏng", template.Lead);
    }
}
