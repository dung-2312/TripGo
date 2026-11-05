using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Tripgo.Application.DTOs;
using Tripgo.Application.DTOs.Admin;
using Tripgo.Application.DTOs.Payment;
using Tripgo.Application.Interfaces.Persistence;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;

namespace Tripgo.Infrastructure.Persistence.Repositories;

public sealed class PaymentGatewayRepository : IPaymentGatewayRepository
{
    private readonly AppDbContext _context;

    public PaymentGatewayRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<(PayableBooking? Booking, string? Problem)> GetPayableBookingAsync(
        string bookingCode, string? ownerIdentifier, long? userId, DateTimeOffset now, CancellationToken ct = default)
    {
        var booking = await _context.Bookings.AsNoTracking()
            .Where(b => b.BookingCode == bookingCode &&
                        ((userId.HasValue && b.UserId == userId.Value) ||
                         (ownerIdentifier != null && b.OwnerIdentifier == ownerIdentifier)))
            .Select(b => new { b.Id, b.BookingCode, b.TotalAmount, b.ExpiresAt, b.Status, b.PaymentStatus, b.IsDemoBooking })
            .FirstOrDefaultAsync(ct);

        if (booking == null) return (null, "Không tìm thấy đơn đặt chỗ hoặc bạn không có quyền thanh toán.");
        if (booking.PaymentStatus == PaymentStatus.Paid) return (null, "Đơn đặt chỗ đã được thanh toán.");
        if (booking.Status != BookingStatus.PendingPayment) return (null, "Đơn đặt chỗ không còn ở trạng thái chờ thanh toán.");
        if (booking.ExpiresAt <= now) return (null, "Đơn đặt chỗ đã hết thời hạn thanh toán.");

        return (new PayableBooking(booking.Id, booking.BookingCode, booking.TotalAmount, booking.ExpiresAt, booking.IsDemoBooking), null);
    }

    public Task<PaymentTransaction?> GetOpenTransactionAsync(long bookingId, string provider, CancellationToken ct = default) =>
        _context.PaymentTransactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.BookingId == bookingId && t.Provider == provider && t.Status == PaymentStatus.Pending, ct);

    public async Task AddPendingTransactionAsync(PaymentTransaction transaction, CancellationToken ct = default)
    {
        _context.ChangeTracker.Clear();
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            transaction.Id = 0;
            await using var dbTransaction = await _context.Database.BeginTransactionAsync(ct);
            var booking = await _context.Bookings.FirstAsync(b => b.Id == transaction.BookingId, ct);
            booking.PaymentStatus = PaymentStatus.Pending;
            _context.PaymentTransactions.Add(transaction);
            await _context.SaveChangesAsync(ct);
            await dbTransaction.CommitAsync(ct);
        });
        _context.ChangeTracker.Clear();
    }

    public async Task SetProviderReferenceAsync(string transactionCode, string? providerReference, CancellationToken ct = default)
    {
        _context.ChangeTracker.Clear();
        var transaction = await _context.PaymentTransactions.FirstOrDefaultAsync(t => t.TransactionCode == transactionCode, ct);
        if (transaction != null)
        {
            transaction.ProviderReference = providerReference;
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task RecordCallbackEventAsync(PaymentWebhookEvent webhookEvent, CancellationToken ct = default)
    {
        _context.ChangeTracker.Clear();
        _context.PaymentWebhookEvents.Add(webhookEvent);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();
    }

    public async Task<PaymentCallbackOutcome> ApplyVerifiedCallbackAsync(
        string provider, VerifiedPaymentCallback callback, DateTimeOffset now, CancellationToken ct = default)
    {
        _context.EnsureWritesTargetIsolatedTestDatabase();
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

                if (callback.EventId != null &&
                    await _context.PaymentWebhookEvents.AnyAsync(e =>
                        e.Provider == provider && e.EventId == callback.EventId && e.SignatureValid, ct))
                {
                    await transaction.RollbackAsync(ct);
                    return new PaymentCallbackOutcome { Outcome = PaymentWebhookEvent.OutcomeDuplicate, Accepted = true };
                }

                var payment = await _context.PaymentTransactions
                    .Include(t => t.Booking).ThenInclude(b => b.BookingSeats).ThenInclude(s => s.TripSeat)
                    .Include(t => t.Booking).ThenInclude(b => b.Trip).ThenInclude(trip => trip.Route).ThenInclude(r => r.Operator)
                    .FirstOrDefaultAsync(t => t.TransactionCode == callback.TransactionCode && t.Provider == provider, ct);

                var outcome = Decide(payment, callback, now);
                _context.PaymentWebhookEvents.Add(new PaymentWebhookEvent
                {
                    Provider = provider,
                    EventId = callback.EventId,
                    TransactionCode = callback.TransactionCode,
                    SignatureValid = true,
                    Outcome = outcome.Outcome,
                    ReportedAmount = callback.Amount,
                    ReportedCurrency = callback.Currency,
                    Detail = outcome.Detail,
                    ReceivedAt = now
                });

                if (outcome.Outcome == PaymentWebhookEvent.OutcomeApplied && payment != null)
                {
                    if (callback.State == ProviderPaymentState.Succeeded)
                    {
                        MarkPaid(payment, callback, now);
                    }
                    else
                    {
                        MarkFailed(payment, callback, now);
                    }
                }

                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return new PaymentCallbackOutcome
                {
                    Outcome = outcome.Outcome,
                    Accepted = true,
                    BookingCode = payment?.Booking.BookingCode
                };
            });
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            // Lost a race with an identical delivery: the unique index kept the outcome single.
            return new PaymentCallbackOutcome { Outcome = PaymentWebhookEvent.OutcomeDuplicate, Accepted = true };
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }
    }

    private static (string Outcome, string? Detail) Decide(PaymentTransaction? payment, VerifiedPaymentCallback callback, DateTimeOffset now)
    {
        if (payment == null)
            return (PaymentWebhookEvent.OutcomeUnknownTransaction, "Không có giao dịch tương ứng trong hệ thống.");

        if (callback.State == ProviderPaymentState.Unknown)
            return (PaymentWebhookEvent.OutcomeIgnored, "Trạng thái không xác định.");

        // Terminal transactions never change again; only an exact repeat reaches this point with a new event ID.
        if (payment.Status != PaymentStatus.Pending)
            return (PaymentWebhookEvent.OutcomeDuplicate, $"Giao dịch đã ở trạng thái {payment.Status}.");

        if (callback.State == ProviderPaymentState.Failed)
            return (PaymentWebhookEvent.OutcomeApplied, null);

        if (callback.Amount != payment.Amount || callback.Amount != payment.Booking.TotalAmount)
            return (PaymentWebhookEvent.OutcomeAmountMismatch, $"Cổng báo {callback.Amount}, hệ thống yêu cầu {payment.Amount}.");

        if (!string.Equals(callback.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
            return (PaymentWebhookEvent.OutcomeCurrencyMismatch, $"Cổng báo {callback.Currency}, hệ thống yêu cầu {payment.Currency}.");

        var booking = payment.Booking;
        if (booking.Status != BookingStatus.PendingPayment || booking.ExpiresAt <= now)
            return (PaymentWebhookEvent.OutcomeLatePayment, "Tiền về sau khi đơn đã hết hạn hoặc bị hủy; cần đối soát và hoàn tiền.");

        return (PaymentWebhookEvent.OutcomeApplied, null);
    }

    private void MarkPaid(PaymentTransaction payment, VerifiedPaymentCallback callback, DateTimeOffset now)
    {
        payment.Status = PaymentStatus.Paid;
        payment.CompletedAt = now;
        payment.ProviderReference = callback.ProviderReference ?? payment.ProviderReference;

        var booking = payment.Booking;
        booking.PaymentStatus = PaymentStatus.Paid;
        booking.Status = BookingStatus.Confirmed;
        booking.ConfirmedAt = now;
        foreach (var seat in booking.BookingSeats)
        {
            seat.IsActive = true;
            if (seat.TripSeat != null)
            {
                seat.TripSeat.Status = TripSeatStatus.Booked;
                seat.TripSeat.UpdatedAt = now;
            }
        }

        QueueEmail(booking, BookingConfirmationEmailOutbox.TypeBookingConfirmation, string.Empty, now);
    }

    private void MarkFailed(PaymentTransaction payment, VerifiedPaymentCallback callback, DateTimeOffset now)
    {
        payment.Status = PaymentStatus.Failed;
        payment.CompletedAt = now;
        payment.ProviderReference = callback.ProviderReference ?? payment.ProviderReference;
        payment.Note = "Cổng thanh toán báo giao dịch không thành công.";

        // The customer can still retry until the payment window ends.
        payment.Booking.PaymentStatus = PaymentStatus.Failed;
        QueueEmail(payment.Booking, BookingConfirmationEmailOutbox.TypePaymentFailed, payment.TransactionCode, now);
    }

    public async Task<RefundableBooking?> GetRefundableBookingAsync(long bookingId, CancellationToken ct = default)
    {
        return await _context.PaymentTransactions.AsNoTracking()
            .Where(t => t.BookingId == bookingId && t.Status == PaymentStatus.Paid)
            .Select(t => new RefundableBooking(
                t.BookingId, t.Booking.BookingCode, t.TransactionCode, t.ProviderReference,
                t.Provider, t.Amount, t.Currency, t.IsSandbox))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<AdminResult> CompleteRefundAsync(long bookingId, string transactionCode, string? refundReference, string reason, DateTimeOffset now, CancellationToken ct = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            var booking = await _context.Bookings
                .Include(b => b.BookingSeats).ThenInclude(s => s.TripSeat)
                .Include(b => b.PaymentTransactions)
                .Include(b => b.Trip).ThenInclude(trip => trip.Route).ThenInclude(r => r.Operator)
                .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
            var payment = booking?.PaymentTransactions.FirstOrDefault(t => t.TransactionCode == transactionCode && t.Status == PaymentStatus.Paid);
            if (booking == null || payment == null)
                return AdminResult.Fail("Giao dịch đã được hoàn tiền hoặc không còn tồn tại.");

            payment.Status = PaymentStatus.Refunded;
            payment.RefundedAt = now;
            payment.RefundReference = refundReference;
            booking.PaymentStatus = PaymentStatus.Refunded;
            booking.Status = BookingStatus.Cancelled;
            booking.CancelledAt = now;
            booking.CancellationReason = reason;

            foreach (var seat in booking.BookingSeats)
            {
                seat.IsActive = false;
                if (seat.TripSeat != null)
                {
                    seat.TripSeat.Status = TripSeatStatus.Available;
                    seat.TripSeat.UpdatedAt = now;
                }
            }

            var credentials = await _context.TicketCredentials
                .Where(c => c.BookingId == bookingId && c.RevokedAt == null)
                .ToListAsync(ct);
            foreach (var credential in credentials)
            {
                credential.RevokedAt = now;
                credential.RevokedReason = "Đơn đã hủy và hoàn tiền.";
            }

            QueueEmail(booking, BookingConfirmationEmailOutbox.TypeBookingCancellation, string.Empty, now);

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return AdminResult.Ok($"Đã hủy đơn {booking.BookingCode} và ghi nhận hoàn tiền.");
        });

        _context.ChangeTracker.Clear();
        return result;
    }

    private void QueueEmail(Booking booking, string messageType, string dedupKey, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(booking.PassengerEmail))
        {
            return;
        }

        _context.BookingConfirmationEmailOutbox.Add(new BookingConfirmationEmailOutbox
        {
            BookingId = booking.Id,
            RecipientEmail = booking.PassengerEmail,
            MessageType = messageType,
            DedupKey = dedupKey,
            Payload = JsonSerializer.Serialize(new BookingConfirmationEmailDto
            {
                BookingCode = booking.BookingCode,
                PassengerName = booking.PassengerName,
                RecipientEmail = booking.PassengerEmail,
                RouteName = booking.Trip?.Route?.Name ?? string.Empty,
                OperatorName = booking.Trip?.Route?.Operator?.Name ?? string.Empty,
                DepartureTime = booking.Trip?.DepartureTime ?? default,
                PickupPoint = booking.PickupPointNote,
                DropoffPoint = booking.DropoffPointNote,
                SeatCodes = booking.BookingSeats.Select(s => s.SeatCode).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
                TotalAmount = booking.TotalAmount,
                MessageType = messageType,
                CancellationReason = booking.CancellationReason
            }),
            Status = BookingConfirmationEmailOutbox.Pending,
            NextAttemptAt = now,
            CreatedAt = now
        });
    }

    private static bool IsDuplicateKey(DbUpdateException exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is SqlException sqlException && sqlException.Number is 2601 or 2627)
                return true;
        }

        return false;
    }
}
