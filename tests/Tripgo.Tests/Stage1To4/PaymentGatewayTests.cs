using Microsoft.EntityFrameworkCore;
using Tripgo.Application.DTOs.Admin;
using Tripgo.Application.DTOs.Payment;
using Tripgo.Application.Interfaces;
using Tripgo.Application.Services;
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
    public async Task DefinitiveInitiationRejection_FailsTransactionAndRetryCreatesANewOne()
    {
        await using var context = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);
        var rejected = new ScriptedProvider((_, _) =>
            Task.FromResult(new ProviderInitiation(false, null, null, "private provider detail")));
        var gateway = _stage.PaymentGateway(context, providers: new[] { rejected });

        var first = await gateway.InitiatePaymentAsync(booking.BookingCode);
        var firstStatus = await context.PaymentTransactions
            .Where(item => item.TransactionCode == first.TransactionCode)
            .Select(item => item.Status)
            .SingleAsync();
        var retry = await gateway.InitiatePaymentAsync(booking.BookingCode);

        Assert.False(first.Success);
        Assert.DoesNotContain("private provider detail", first.Message, StringComparison.Ordinal);
        Assert.Equal(PaymentStatus.Failed, firstStatus);
        Assert.False(string.Equals(first.TransactionCode, retry.TransactionCode, StringComparison.Ordinal));
        Assert.Equal(2, await context.PaymentTransactions.CountAsync(item => item.BookingId == booking.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AmbiguousInitiationFailure_KeepsPendingAndRetryUsesSameIdempotencyKey(bool timeout)
    {
        await using var context = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);
        var calls = 0;
        var provider = new ScriptedProvider((request, _) =>
        {
            Interlocked.Increment(ref calls);
            return calls == 1
                ? Task.FromException<ProviderInitiation>(
                    timeout ? new TimeoutException("private timeout detail") : new InvalidOperationException("private network detail"))
                : Task.FromResult(new ProviderInitiation(true, "provider-reference", "https://sandbox.invalid/pay", null));
        });
        var gateway = _stage.PaymentGateway(context, providers: new[] { provider });

        var first = await gateway.InitiatePaymentAsync(booking.BookingCode);
        var pendingStatus = await context.PaymentTransactions
            .Where(item => item.TransactionCode == first.TransactionCode)
            .Select(item => item.Status)
            .SingleAsync();
        var retry = await gateway.InitiatePaymentAsync(booking.BookingCode);

        Assert.False(first.Success);
        Assert.Equal(PaymentStatus.Pending, pendingStatus);
        Assert.True(retry.Success, retry.Message);
        Assert.Equal(first.TransactionCode, retry.TransactionCode);
        Assert.Equal(1, await context.PaymentTransactions.CountAsync(item => item.BookingId == booking.Id));
        Assert.Equal(2, provider.TransactionCodes.Count);
        Assert.All(provider.TransactionCodes, code => Assert.Equal(first.TransactionCode, code));
    }

    [Fact]
    public async Task LateCallbackAfterDefinitiveInitiationRejection_CannotConfirmBooking()
    {
        await using var context = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);
        var provider = new ScriptedProvider((_, _) =>
            Task.FromResult(new ProviderInitiation(false, null, null, null)));
        var gateway = _stage.PaymentGateway(context, providers: new[] { provider });
        var initiation = await gateway.InitiatePaymentAsync(booking.BookingCode);
        var callback = Stage.Gateway.CreateSignedCallback(NewEvent(), initiation.TransactionCode!, booking.TotalAmount, "VND", "succeeded");

        var outcome = await gateway.HandleCallbackAsync(Stage.Gateway.Name, callback);

        Assert.Equal(PaymentWebhookEvent.OutcomeDuplicate, outcome.Outcome);
        Assert.Equal(PaymentStatus.Failed, await context.PaymentTransactions
            .Where(item => item.TransactionCode == initiation.TransactionCode)
            .Select(item => item.Status)
            .SingleAsync());
        Assert.NotEqual(BookingStatus.Confirmed, await context.Bookings
            .Where(item => item.Id == booking.Id)
            .Select(item => item.Status)
            .SingleAsync());
    }

    [Fact]
    public async Task CallbackCannotReverseCompletedPayment()
    {
        var (context, gateway, payment, bookingId, amount) = await StartAsync();
        await using var _ = context;
        Assert.Equal(PaymentWebhookEvent.OutcomeApplied, (await SendAsync(gateway, NewEvent(), payment.TransactionCode!, amount)).Outcome);

        var failedCallback = await SendAsync(gateway, NewEvent(), payment.TransactionCode!, amount, status: "failed");

        Assert.Equal(PaymentWebhookEvent.OutcomeDuplicate, failedCallback.Outcome);
        Assert.Equal(PaymentStatus.Paid, await context.Bookings
            .Where(item => item.Id == bookingId)
            .Select(item => item.PaymentStatus)
            .SingleAsync());
    }

    [Fact]
    public async Task ConcurrentInitiation_CreatesOnlyOneOpenTransaction()
    {
        await using var context = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);

        var attempts = Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var attemptContext = Stage.NewContext();
            return await _stage.PaymentGateway(attemptContext).InitiatePaymentAsync(booking.BookingCode);
        });
        var results = await Task.WhenAll(attempts);

        Assert.All(results, result => Assert.True(result.Success, result.Message));
        Assert.Single(results.Select(result => result.TransactionCode).Distinct());
        Assert.Equal(1, await context.PaymentTransactions.CountAsync(t => t.BookingId == booking.Id && t.Status == PaymentStatus.Pending));
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
    public async Task SandboxSimulation_UsesVerifiedCallback_AndConfirmsTheBooking()
    {
        var (context, gateway, _, bookingId, _) = await StartAsync();
        await using var _ = context;
        var bookingCode = await context.Bookings.Where(b => b.Id == bookingId).Select(b => b.BookingCode).SingleAsync();

        var result = await gateway.SimulateSandboxPaymentAsync(bookingCode, succeeded: true);

        Assert.True(result.Success, result.Message);
        Assert.True(result.PaymentSucceeded);
        Assert.Equal(PaymentWebhookEvent.OutcomeApplied, result.Outcome);
        Assert.NotNull(result.TransactionCode);
        Assert.Equal(BookingStatus.Confirmed, await context.Bookings.Where(b => b.Id == bookingId).Select(b => b.Status).SingleAsync());
        Assert.Equal(PaymentStatus.Paid, await context.Bookings.Where(b => b.Id == bookingId).Select(b => b.PaymentStatus).SingleAsync());
        Assert.True(await context.PaymentWebhookEvents.AnyAsync(e =>
            e.TransactionCode == result.TransactionCode && e.SignatureValid && e.Outcome == PaymentWebhookEvent.OutcomeApplied));
    }

    [Fact]
    public async Task SandboxSimulation_FailureLeavesBookingUnpaidAndRetryable()
    {
        var (context, gateway, _, bookingId, _) = await StartAsync();
        await using var _ = context;
        var bookingCode = await context.Bookings.Where(b => b.Id == bookingId).Select(b => b.BookingCode).SingleAsync();

        var result = await gateway.SimulateSandboxPaymentAsync(bookingCode, succeeded: false);

        Assert.True(result.Success, result.Message);
        Assert.False(result.PaymentSucceeded);
        Assert.Equal(PaymentWebhookEvent.OutcomeApplied, result.Outcome);
        Assert.Equal(BookingStatus.PendingPayment, await context.Bookings.Where(b => b.Id == bookingId).Select(b => b.Status).SingleAsync());
        Assert.Equal(PaymentStatus.Failed, await context.Bookings.Where(b => b.Id == bookingId).Select(b => b.PaymentStatus).SingleAsync());
        Assert.True((await gateway.InitiatePaymentAsync(bookingCode)).Success);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedCallbackWithMismatchedAmountOrCurrency_DoesNotChangePaymentState(bool mismatchAmount)
    {
        var (context, gateway, payment, bookingId, amount) = await StartAsync();
        await using var _ = context;

        var outcome = await SendAsync(
            gateway,
            NewEvent(),
            payment.TransactionCode!,
            mismatchAmount ? amount + 1m : amount,
            currency: mismatchAmount ? "VND" : "USD",
            status: "failed");

        Assert.Equal(
            mismatchAmount ? PaymentWebhookEvent.OutcomeAmountMismatch : PaymentWebhookEvent.OutcomeCurrencyMismatch,
            outcome.Outcome);
        Assert.Equal(PaymentStatus.Pending, await context.PaymentTransactions
            .Where(item => item.TransactionCode == payment.TransactionCode)
            .Select(item => item.Status)
            .SingleAsync());
        Assert.Equal(BookingStatus.PendingPayment, await context.Bookings
            .Where(item => item.Id == bookingId)
            .Select(item => item.Status)
            .SingleAsync());
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

    private sealed class ScriptedProvider : IPaymentProvider
    {
        private readonly Func<ProviderInitiationRequest, CancellationToken, Task<ProviderInitiation>> _initiate;
        private readonly System.Collections.Concurrent.ConcurrentBag<string> _transactionCodes = new();

        public ScriptedProvider(Func<ProviderInitiationRequest, CancellationToken, Task<ProviderInitiation>> initiate) =>
            _initiate = initiate;

        public string Name => Stage.Gateway.Name;
        public bool IsSandbox => true;
        public bool SupportsRefund => false;
        public IReadOnlyCollection<string> TransactionCodes => _transactionCodes;

        public Task<ProviderInitiation> InitiateAsync(ProviderInitiationRequest request, CancellationToken ct = default)
        {
            _transactionCodes.Add(request.TransactionCode);
            return _initiate(request, ct);
        }

        public VerifiedPaymentCallback VerifyCallback(PaymentCallbackRequest request) =>
            Stage.Gateway.VerifyCallback(request);

        public Task<ProviderRefund> RefundAsync(ProviderRefundRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ProviderRefund(false, null, "Refund is not supported."));
    }
}
