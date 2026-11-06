using Tripgo.Application.DTOs.Admin;
using Tripgo.Application.DTOs.Payment;
using Tripgo.Application.Interfaces;
using Tripgo.Application.Interfaces.Persistence;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Tripgo.Application.Services;

public sealed class PaymentGatewayService : IPaymentGatewayService
{
    private const string Currency = "VND";

    private readonly IPaymentGatewayRepository _repository;
    private readonly IBookingSessionContext _session;
    private readonly IAdminAccessGuard _guard;
    private readonly Dictionary<string, IPaymentProvider> _providers;
    private readonly ILogger<PaymentGatewayService>? _logger;

    public PaymentGatewayService(
        IPaymentGatewayRepository repository,
        IBookingSessionContext session,
        IAdminAccessGuard guard,
        IEnumerable<IPaymentProvider> providers,
        ILogger<PaymentGatewayService>? logger = null)
    {
        _repository = repository;
        _session = session;
        _guard = guard;
        _providers = providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public IReadOnlyList<PaymentProviderInfo> GetProviders() =>
        _providers.Values.Select(p => new PaymentProviderInfo(p.Name, p.IsSandbox, p.SupportsRefund, p.IsAvailable, p.DisplayName)).ToList();

    public async Task<SandboxPaymentResult> SimulateSandboxPaymentAsync(string bookingCode, bool succeeded, CancellationToken ct = default)
    {
        var provider = _providers.Values.OfType<ISandboxPaymentProvider>().FirstOrDefault(p => p.IsSandbox);
        if (provider == null)
        {
            return new SandboxPaymentResult { Message = "Thanh toán sandbox chưa được cấu hình." };
        }

        var initiation = await InitiatePaymentAsync(bookingCode, provider.Name, ct);
        if (!initiation.Success || string.IsNullOrWhiteSpace(initiation.TransactionCode))
        {
            return new SandboxPaymentResult
            {
                Message = initiation.Message,
                BookingCode = bookingCode
            };
        }

        var callback = provider.CreateSimulationCallback(
            initiation.TransactionCode,
            initiation.Amount,
            initiation.Currency,
            succeeded);
        var outcome = await HandleCallbackAsync(provider.Name, callback, ct);
        var applied = outcome.Accepted && outcome.Outcome == PaymentWebhookEvent.OutcomeApplied;

        return new SandboxPaymentResult
        {
            Success = applied,
            PaymentSucceeded = applied && succeeded,
            Message = applied
                ? succeeded ? "Thanh toán sandbox đã được xác nhận." : "Giao dịch sandbox đã bị từ chối."
                : $"Không thể áp dụng kết quả thanh toán sandbox ({outcome.Outcome}).",
            BookingCode = outcome.BookingCode ?? bookingCode,
            TransactionCode = initiation.TransactionCode,
            Outcome = outcome.Outcome
        };
    }

    public async Task<PaymentInitiationResult> InitiatePaymentAsync(string bookingCode, string? providerName = null, CancellationToken ct = default)
    {
        var provider = ResolveProvider(providerName);
        if (provider == null)
        {
            return new PaymentInitiationResult { Message = "Chưa có cổng thanh toán nào được cấu hình." };
        }

        if (!provider.IsAvailable)
        {
            return new PaymentInitiationResult { Message = $"Cổng thanh toán {provider.DisplayName} đang trong quá trình tích hợp và chưa khả dụng." };
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
        var pending = open == null
            ? await _repository.AddPendingTransactionAsync(new PaymentTransaction
            {
                BookingId = booking.BookingId,
                TransactionCode = $"TXN-{provider.Name}-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}",
                Provider = provider.Name,
                PaymentMethod = provider.Name,
                Amount = booking.TotalAmount,
                Currency = Currency,
                Status = PaymentStatus.Pending,
                IsSandbox = provider.IsSandbox,
                CreatedAt = now,
                ExpiresAt = booking.ExpiresAt
            }, ct)
            : new PendingPaymentTransactionResult(open, Created: false);

        ProviderInitiation initiation;
        try
        {
            // TransactionCode is the provider idempotency key. Retrying an uncertain initiation
            // must reuse it so a provider cannot create a second charge.
            initiation = await provider.InitiateAsync(
                new ProviderInitiationRequest(
                    pending.Transaction.TransactionCode,
                    booking.BookingCode,
                    booking.TotalAmount,
                    Currency,
                    booking.ExpiresAt),
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A timeout or transport exception cannot establish whether the provider accepted
            // the request. Keep the transaction pending; a retry uses the same idempotency key.
            _logger?.LogWarning(
                "Payment provider {Provider} initiation outcome is unknown for transaction {TransactionCode} ({ExceptionType}); transaction remains pending for idempotent retry.",
                provider.Name,
                pending.Transaction.TransactionCode,
                exception.GetType().Name);
            return new PaymentInitiationResult
            {
                Message = "Chưa xác định được kết quả khởi tạo thanh toán. Vui lòng thử lại để kiểm tra giao dịch.",
                TransactionCode = pending.Transaction.TransactionCode,
                Amount = pending.Transaction.Amount,
                Currency = pending.Transaction.Currency,
                IsSandbox = pending.Transaction.IsSandbox
            };
        }

        if (!initiation.Accepted)
        {
            await _repository.FailInitiationAsync(
                pending.Transaction.TransactionCode,
                "Provider definitively rejected payment initiation.",
                DateTimeOffset.UtcNow,
                ct);
            return new PaymentInitiationResult
            {
                Message = "Cổng thanh toán đã từ chối khởi tạo giao dịch.",
                TransactionCode = pending.Transaction.TransactionCode,
                Amount = pending.Transaction.Amount,
                Currency = pending.Transaction.Currency,
                IsSandbox = pending.Transaction.IsSandbox
            };
        }

        await _repository.SetProviderReferenceAsync(pending.Transaction.TransactionCode, initiation.ProviderReference, ct);
        return new PaymentInitiationResult
        {
            Success = true,
            Message = "Đã khởi tạo giao dịch.",
            TransactionCode = pending.Transaction.TransactionCode,
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

        if (!provider.IsAvailable)
        {
            return new PaymentCallbackOutcome { Outcome = "ProviderUnavailable" };
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
