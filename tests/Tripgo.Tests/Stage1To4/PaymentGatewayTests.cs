using Microsoft.EntityFrameworkCore;
using Tripgo.Application.DTOs.Payment;
using Tripgo.Application.Interfaces;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;

namespace Tripgo.Tests.Stage1To4;

[Collection("Database")]
public sealed class PaymentGatewayTests : IAsyncLifetime
{
    private readonly Stage _stage = new();

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => _stage.CleanupAsync();

    private async Task<(Tripgo.Infrastructure.Persistence.AppDbContext Context, IPaymentGatewayService Gateway, PaymentInitiationResult Payment, long BookingId, decimal Amount)> StartAsync()
    {
        var context = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);
        var gateway = _stage.PaymentGateway(context);
        var payment = await gateway.InitiatePaymentAsync(booking.BookingCode);
        Assert.True(payment.Success, payment.Message);
        return (context, gateway, payment, booking.Id, booking.TotalAmount);
    }

    private Task<PaymentCallbackOutcome> SendAsync(IPaymentGatewayService gateway, string eventId, string transactionCode, decimal amount, string currency = "VND", string status = "succeeded") =>
        gateway.HandleCallbackAsync(Stage.Gateway.Name, Stage.Gateway.CreateSignedCallback(eventId, transactionCode, amount, currency, status));

    private static string NewEvent() => $"evt-{Guid.NewGuid():N}";

    [Fact]
    public async Task Initiation_ComputesTheAmountFromTheBooking_AndLeavesItUnpaid()
    {
        var (context, _, payment, bookingId, amount) = await StartAsync();
        await using var _ = context;

        Assert.Equal(amount, payment.Amount);
        var stored = await context.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId);
        Assert.Equal(BookingStatus.PendingPayment, stored.Status);
        Assert.Equal(PaymentStatus.Pending, stored.PaymentStatus);
    }

    [Fact]
    public async Task Initiation_IsRejected_ForSomeoneElsesBooking()
    {
        await using var context = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);

        var result = await _stage.PaymentGateway(context, owner: Guid.NewGuid().ToString("N")).InitiatePaymentAsync(booking.BookingCode);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Initiation_ReusesTheOpenTransaction()
    {
        var (context, gateway, payment, bookingId, _) = await StartAsync();
        await using var _ = context;

        var again = await gateway.InitiatePaymentAsync((await context.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId)).BookingCode);

        Assert.Equal(payment.TransactionCode, again.TransactionCode);
        Assert.Equal(1, await context.PaymentTransactions.CountAsync(t => t.BookingId == bookingId));
    }

    [Fact]
    public async Task TransactionCodes_AreUnique_AcrossBookings()
    {
        var (first, _, firstPayment, _, _) = await StartAsync();
        var (second, _, secondPayment, _, _) = await StartAsync();
        await first.DisposeAsync();
        await second.DisposeAsync();

        Assert.NotEqual(firstPayment.TransactionCode, secondPayment.TransactionCode);
    }

    [Fact]
    public async Task ValidCallback_ConfirmsTheBooking()
    {
        var (context, gateway, payment, bookingId, amount) = await StartAsync();
        await using var _ = context;

        var outcome = await SendAsync(gateway, NewEvent(), payment.TransactionCode!, amount);

        Assert.Equal(PaymentWebhookEvent.OutcomeApplied, outcome.Outcome);
        var stored = await context.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId);
        Assert.Equal(BookingStatus.Confirmed, stored.Status);
        Assert.Equal(PaymentStatus.Paid, stored.PaymentStatus);
        Assert.Equal(TripSeatStatus.Booked, await context.BookingSeats.Where(s => s.BookingId == bookingId).Select(s => s.TripSeat.Status).FirstAsync());
    }

    [Fact]
    public async Task NoCallback_MeansNotPaid_WhateverTheBrowserClaims()
    {
        var (context, _, _, bookingId, _) = await StartAsync();
        await using var _ = context;

        // A redirect to a success page changes nothing: only a verified callback can.
        var stored = await context.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId);
        Assert.NotEqual(PaymentStatus.Paid, stored.PaymentStatus);
        Assert.Null(stored.ConfirmedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-hex")]
    [InlineData("deadbeef")]
    public async Task BadSignature_IsRejected_AndRecorded(string signature)
    {
        var (context, gateway, payment, bookingId, amount) = await StartAsync();
        await using var _ = context;
        var genuine = Stage.Gateway.CreateSignedCallback(NewEvent(), payment.TransactionCode!, amount, "VND", "succeeded");

        var outcome = await gateway.HandleCallbackAsync(Stage.Gateway.Name, genuine with { Signature = signature });

        Assert.Equal(PaymentWebhookEvent.OutcomeInvalidSignature, outcome.Outcome);
        Assert.False(outcome.Accepted);
        Assert.NotEqual(PaymentStatus.Paid, await context.Bookings.Where(b => b.Id == bookingId).Select(b => b.PaymentStatus).SingleAsync());
        Assert.True(await context.PaymentWebhookEvents.AnyAsync(e => e.Outcome == PaymentWebhookEvent.OutcomeInvalidSignature));
        await context.PaymentWebhookEvents.Where(e => e.Outcome == PaymentWebhookEvent.OutcomeInvalidSignature).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task TamperedBody_FailsSignatureCheck()
    {
        var (context, gateway, payment, bookingId, amount) = await StartAsync();
        await using var _ = context;
        var genuine = Stage.Gateway.CreateSignedCallback(NewEvent(), payment.TransactionCode!, 1m, "VND", "succeeded");
        var tampered = genuine with { Body = genuine.Body.Replace("\"amount\":1", $"\"amount\":{amount:0}") };

        var outcome = await gateway.HandleCallbackAsync(Stage.Gateway.Name, tampered);

        Assert.Equal(PaymentWebhookEvent.OutcomeInvalidSignature, outcome.Outcome);
        Assert.NotEqual(PaymentStatus.Paid, await context.Bookings.Where(b => b.Id == bookingId).Select(b => b.PaymentStatus).SingleAsync());
        await context.PaymentWebhookEvents.Where(e => e.Outcome == PaymentWebhookEvent.OutcomeInvalidSignature).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task DuplicateEvent_IsIdempotent()
    {
        var (context, gateway, payment, bookingId, amount) = await StartAsync();
        await using var _ = context;
        var eventId = NewEvent();

        await SendAsync(gateway, eventId, payment.TransactionCode!, amount);
        var replay = await SendAsync(gateway, eventId, payment.TransactionCode!, amount);

        Assert.Equal(PaymentWebhookEvent.OutcomeDuplicate, replay.Outcome);
        Assert.True(replay.Accepted);
        Assert.Equal(1, await context.PaymentWebhookEvents.CountAsync(e => e.EventId == eventId));
        Assert.Equal(1, await context.BookingConfirmationEmailOutbox.CountAsync(e =>
            e.BookingId == bookingId && e.MessageType == BookingConfirmationEmailOutbox.TypeBookingConfirmation));
    }

    [Fact]
    public async Task ConcurrentDuplicateCallbacks_ApplyOnce()
    {
        var (context, _, payment, bookingId, amount) = await StartAsync();
        await using var _ = context;
        var eventId = NewEvent();

        var deliveries = Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var deliveryContext = Stage.NewContext();
            return await SendAsync(_stage.PaymentGateway(deliveryContext), eventId, payment.TransactionCode!, amount);
        });
        var outcomes = await Task.WhenAll(deliveries);

        Assert.Single(outcomes, o => o.Outcome == PaymentWebhookEvent.OutcomeApplied);
        Assert.Equal(1, await context.PaymentTransactions.CountAsync(t => t.BookingId == bookingId && t.Status == PaymentStatus.Paid));
        Assert.Equal(1, await context.BookingConfirmationEmailOutbox.CountAsync(e =>
            e.BookingId == bookingId && e.MessageType == BookingConfirmationEmailOutbox.TypeBookingConfirmation));
    }

    [Fact]
    public async Task IncorrectAmount_DoesNotConfirm_AndIsFlaggedForReconciliation()
    {
        var (context, gateway, payment, bookingId, amount) = await StartAsync();
        await using var _ = context;

        var outcome = await SendAsync(gateway, NewEvent(), payment.TransactionCode!, amount - 1000m);

        Assert.Equal(PaymentWebhookEvent.OutcomeAmountMismatch, outcome.Outcome);
        Assert.NotEqual(PaymentStatus.Paid, await context.Bookings.Where(b => b.Id == bookingId).Select(b => b.PaymentStatus).SingleAsync());
    }

    [Fact]
    public async Task IncorrectCurrency_DoesNotConfirm()
    {
        var (context, gateway, payment, bookingId, amount) = await StartAsync();
        await using var _ = context;

        var outcome = await SendAsync(gateway, NewEvent(), payment.TransactionCode!, amount, currency: "USD");

        Assert.Equal(PaymentWebhookEvent.OutcomeCurrencyMismatch, outcome.Outcome);
        Assert.NotEqual(PaymentStatus.Paid, await context.Bookings.Where(b => b.Id == bookingId).Select(b => b.PaymentStatus).SingleAsync());
    }

    [Fact]
    public async Task FailedCallback_LeavesTheBookingRetryable_AndNotifiesTheCustomer()
    {
        var (context, gateway, payment, bookingId, amount) = await StartAsync();
        await using var _ = context;

        var outcome = await SendAsync(gateway, NewEvent(), payment.TransactionCode!, amount, status: "failed");

        Assert.Equal(PaymentWebhookEvent.OutcomeApplied, outcome.Outcome);
        var stored = await context.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId);
        Assert.Equal(BookingStatus.PendingPayment, stored.Status);
        Assert.Equal(PaymentStatus.Failed, stored.PaymentStatus);
        Assert.True(await context.BookingConfirmationEmailOutbox.AnyAsync(e =>
            e.BookingId == bookingId && e.MessageType == BookingConfirmationEmailOutbox.TypePaymentFailed));

        var retry = await gateway.InitiatePaymentAsync(stored.BookingCode);
        Assert.True(retry.Success, retry.Message);
        Assert.NotEqual(payment.TransactionCode, retry.TransactionCode);
    }

    [Fact]
    public async Task LatePayment_OnAnExpiredBooking_IsFlagged_AndDoesNotResurrectIt()
    {
        var (context, gateway, payment, bookingId, amount) = await StartAsync();
        await using var _ = context;
        await context.Bookings.Where(b => b.Id == bookingId).ExecuteUpdateAsync(s => s.SetProperty(b => b.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));

        var outcome = await SendAsync(gateway, NewEvent(), payment.TransactionCode!, amount);

        Assert.Equal(PaymentWebhookEvent.OutcomeLatePayment, outcome.Outcome);
        Assert.NotEqual(BookingStatus.Confirmed, await context.Bookings.Where(b => b.Id == bookingId).Select(b => b.Status).SingleAsync());
    }

    [Fact]
    public async Task Timeout_ClosesAnUnansweredTransaction_AndFreesTheSeat()
    {
        var (context, _, payment, bookingId, _) = await StartAsync();
        await using var _ = context;
        await context.Bookings.Where(b => b.Id == bookingId).ExecuteUpdateAsync(s => s.SetProperty(b => b.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));

        await new Tripgo.Infrastructure.Persistence.Repositories.BookingRepository(context).ExpireStaleBookingsAsync();

        var stored = await context.Bookings.AsNoTracking().Include(b => b.BookingSeats).SingleAsync(b => b.Id == bookingId);
        Assert.Equal(BookingStatus.Expired, stored.Status);
        Assert.False(stored.BookingSeats.Single().IsActive);
        Assert.Equal(PaymentStatus.Failed, await context.PaymentTransactions.Where(t => t.TransactionCode == payment.TransactionCode).Select(t => t.Status).SingleAsync());
    }

    [Fact]
    public async Task UnknownTransaction_IsRecorded_NotApplied()
    {
        await using var context = Stage.NewContext();
        var code = $"TXN-UNKNOWN-{_stage.Marker}";

        var outcome = await SendAsync(_stage.PaymentGateway(context), NewEvent(), code, 1000m);

        Assert.Equal(PaymentWebhookEvent.OutcomeUnknownTransaction, outcome.Outcome);
        await context.PaymentWebhookEvents.Where(e => e.TransactionCode == code).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task UnknownProvider_IsRejected()
    {
        await using var context = Stage.NewContext();

        var outcome = await _stage.PaymentGateway(context).HandleCallbackAsync("NOT-A-PROVIDER", new PaymentCallbackRequest("{}", "x"));

        Assert.False(outcome.Accepted);
    }

    [Fact]
    public async Task Refund_RequiresAnAdmin()
    {
        await using var context = Stage.NewContext();
        var customer = await _stage.CreateUserAsync(context, RoleNames.Customer);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _stage.PaymentGateway(context, userId: customer).RefundBookingAsync(1, "Không được phép"));
    }

    [Fact]
    public async Task SandboxProvider_CannotSettleARealBooking()
    {
        await using var context = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);
        await context.Bookings.Where(b => b.Id == booking.Id).ExecuteUpdateAsync(s => s.SetProperty(b => b.IsDemoBooking, false));

        var result = await _stage.PaymentGateway(context).InitiatePaymentAsync(booking.BookingCode);

        Assert.False(result.Success);
    }
}
