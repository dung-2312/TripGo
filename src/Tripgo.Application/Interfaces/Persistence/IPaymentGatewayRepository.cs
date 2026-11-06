using Tripgo.Application.DTOs.Admin;
using Tripgo.Application.DTOs.Payment;
using Tripgo.Domain.Entities;

namespace Tripgo.Application.Interfaces.Persistence;

public sealed record PayableBooking(long BookingId, string BookingCode, decimal TotalAmount, DateTimeOffset ExpiresAt, bool IsSandbox);

public sealed record RefundableBooking(
    long BookingId, string BookingCode, string TransactionCode, string? ProviderReference,
    string Provider, decimal Amount, string Currency, bool IsSandbox);

public sealed record PendingPaymentTransactionResult(PaymentTransaction Transaction, bool Created);

public interface IPaymentGatewayRepository
{
    /// <summary>Returns the booking only for its owner, and only while it can still be paid.</summary>
    Task<(PayableBooking? Booking, string? Problem)> GetPayableBookingAsync(string bookingCode, string? ownerIdentifier, long? userId, DateTimeOffset now, CancellationToken ct = default);
    Task<PaymentTransaction?> GetOpenTransactionAsync(long bookingId, string provider, CancellationToken ct = default);
    Task<PendingPaymentTransactionResult> AddPendingTransactionAsync(PaymentTransaction transaction, CancellationToken ct = default);
    Task<bool> FailInitiationAsync(string transactionCode, string reason, DateTimeOffset completedAt, CancellationToken ct = default);
    Task SetProviderReferenceAsync(string transactionCode, string? providerReference, CancellationToken ct = default);
    Task RecordCallbackEventAsync(PaymentWebhookEvent webhookEvent, CancellationToken ct = default);

    /// <summary>Applies a verified callback atomically and idempotently, and records what happened.</summary>
    Task<PaymentCallbackOutcome> ApplyVerifiedCallbackAsync(string provider, VerifiedPaymentCallback callback, DateTimeOffset now, CancellationToken ct = default);

    Task<RefundableBooking?> GetRefundableBookingAsync(long bookingId, CancellationToken ct = default);
    Task<AdminResult> CompleteRefundAsync(long bookingId, string transactionCode, string? refundReference, string reason, DateTimeOffset now, CancellationToken ct = default);
}
