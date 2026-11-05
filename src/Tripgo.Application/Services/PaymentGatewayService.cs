using Tripgo.Application.DTOs.Admin;
using Tripgo.Application.DTOs.Payment;
using Tripgo.Application.Interfaces;
using Tripgo.Application.Interfaces.Persistence;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;

namespace Tripgo.Application.Services;

public sealed class PaymentGatewayService : IPaymentGatewayService
{
    private const string Currency = "VND";

    private readonly IPaymentGatewayRepository _repository;
    private readonly IBookingSessionContext _session;
    private readonly IAdminAccessGuard _guard;
    private readonly Dictionary<string, IPaymentProvider> _providers;

    public PaymentGatewayService(
        IPaymentGatewayRepository repository,
        IBookingSessionContext session,
        IAdminAccessGuard guard,
        IEnumerable<IPaymentProvider> providers)
    {
        _repository = repository;
        _session = session;
        _guard = guard;
        _providers = providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<PaymentProviderInfo> GetProviders() =>
        _providers.Values.Select(p => new PaymentProviderInfo(p.Name, p.IsSandbox, p.SupportsRefund)).ToList();

    public async Task<PaymentInitiationResult> InitiatePaymentAsync(string bookingCode, string? providerName = null, CancellationToken ct = default)
    {
        var provider = ResolveProvider(providerName);
        if (provider == null)
        {
            return new PaymentInitiationResult { Message = "Chưa có cổng thanh toán nào được cấu hình." };
        }

        var ownerIdentifier = await _session.GetOwnerIdentifierAsync();
        var userId = await _session.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(ownerIdentifier) && !userId.HasValue)
        {
            return new PaymentInitiationResult { Message = "Phiên giao dịch không hợp lệ." };
        }

        var now = DateTimeOffset.UtcNow;
        var (booking, problem) = await _repository.GetPayableBookingAsync(bookingCode?.Trim() ?? string.Empty, ownerIdentifier, userId, now, ct);
        if (booking == null)
        {
            return new PaymentInitiationResult { Message = problem ?? "Không tìm thấy đơn đặt chỗ." };
        }

        // A sandbox booking must never reach a real provider, and a real provider never settles sandbox data.
        if (booking.IsSandbox != provider.IsSandbox)
        {
            return new PaymentInitiationResult { Message = "Đơn đặt chỗ này không dùng được với cổng thanh toán đã chọn." };
        }

        var open = await _repository.GetOpenTransactionAsync(booking.BookingId, provider.Name, ct);
        if (open != null)
        {
            return new PaymentInitiationResult
            {
                Success = true,
                Message = "Tiếp tục giao dịch đang mở.",
                TransactionCode = open.TransactionCode,
                Amount = open.Amount,
                Currency = open.Currency,
                IsSandbox = open.IsSandbox
            };
        }

        var transactionCode = $"TXN-{provider.Name}-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        await _repository.AddPendingTransactionAsync(new PaymentTransaction
        {
            BookingId = booking.BookingId,
            TransactionCode = transactionCode,
            Provider = provider.Name,
            PaymentMethod = provider.Name,
            Amount = booking.TotalAmount,
            Currency = Currency,
            Status = PaymentStatus.Pending,
            IsSandbox = provider.IsSandbox,
            CreatedAt = now,
            ExpiresAt = booking.ExpiresAt
        }, ct);

        var initiation = await provider.InitiateAsync(
            new ProviderInitiationRequest(transactionCode, booking.BookingCode, booking.TotalAmount, Currency, booking.ExpiresAt), ct);
        if (!initiation.Accepted)
        {
            return new PaymentInitiationResult { Message = initiation.Error ?? "Cổng thanh toán từ chối yêu cầu." };
        }

        await _repository.SetProviderReferenceAsync(transactionCode, initiation.ProviderReference, ct);
        return new PaymentInitiationResult
        {
            Success = true,
            Message = "Đã khởi tạo giao dịch.",
            TransactionCode = transactionCode,
            RedirectUrl = initiation.RedirectUrl,
            Amount = booking.TotalAmount,
            Currency = Currency,
            IsSandbox = provider.IsSandbox
        };
    }

    public async Task<PaymentCallbackOutcome> HandleCallbackAsync(string providerName, PaymentCallbackRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_providers.TryGetValue(providerName ?? string.Empty, out var provider))
        {
            return new PaymentCallbackOutcome { Outcome = "UnknownProvider" };
        }

        var verified = provider.VerifyCallback(request);
        if (!verified.SignatureValid)
        {
            await _repository.RecordCallbackEventAsync(new PaymentWebhookEvent
            {
                Provider = provider.Name,
                SignatureValid = false,
                Outcome = PaymentWebhookEvent.OutcomeInvalidSignature,
                Detail = Truncate(verified.Error, 500),
                ReceivedAt = DateTimeOffset.UtcNow
            }, ct);
            return new PaymentCallbackOutcome { Outcome = PaymentWebhookEvent.OutcomeInvalidSignature };
        }

        return await _repository.ApplyVerifiedCallbackAsync(provider.Name, verified, DateTimeOffset.UtcNow, ct);
    }

    public async Task<AdminResult> RefundBookingAsync(long bookingId, string reason, CancellationToken ct = default)
    {
        await _guard.RequireRoleAsync(RoleNames.Admin);

        var refundable = await _repository.GetRefundableBookingAsync(bookingId, ct);
        if (refundable == null)
        {
            return AdminResult.Fail("Đơn này không có giao dịch đã thanh toán để hoàn tiền.");
        }

        if (!_providers.TryGetValue(refundable.Provider, out var provider))
        {
            // Older sandbox rows predate named providers; anything else needs a human with provider access.
            provider = refundable.IsSandbox ? _providers.Values.FirstOrDefault(p => p.IsSandbox) : null;
        }

        if (provider == null || !provider.SupportsRefund)
        {
            return AdminResult.Fail($"Cổng thanh toán của đơn {refundable.BookingCode} chưa hỗ trợ hoàn tiền tự động. Cần xử lý trực tiếp với nhà cung cấp.");
        }

        var refund = await provider.RefundAsync(new ProviderRefundRequest(
            refundable.TransactionCode, refundable.ProviderReference, refundable.Amount, refundable.Currency, reason), ct);
        if (!refund.Accepted)
        {
            return AdminResult.Fail($"Cổng thanh toán từ chối hoàn tiền đơn {refundable.BookingCode}: {refund.Error}");
        }

        return await _repository.CompleteRefundAsync(bookingId, refundable.TransactionCode, refund.RefundReference, reason, DateTimeOffset.UtcNow, ct);
    }

    private IPaymentProvider? ResolveProvider(string? providerName)
    {
        if (!string.IsNullOrWhiteSpace(providerName))
        {
            return _providers.GetValueOrDefault(providerName);
        }

        return _providers.Values.FirstOrDefault();
    }

    private static string? Truncate(string? value, int length) =>
        value is { Length: var actual } && actual > length ? value[..length] : value;
}
