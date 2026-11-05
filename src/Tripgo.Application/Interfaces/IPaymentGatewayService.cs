using Tripgo.Application.DTOs.Admin;
using Tripgo.Application.DTOs.Payment;

namespace Tripgo.Application.Interfaces;

/// <summary>
/// The provider-independent payment flow. A booking only becomes paid here through a callback whose
/// signature, amount and currency have been verified; a browser redirect can never confirm a payment.
/// </summary>
public interface IPaymentGatewayService
{
    Task<PaymentInitiationResult> InitiatePaymentAsync(string bookingCode, string? providerName = null, CancellationToken ct = default);
    Task<PaymentCallbackOutcome> HandleCallbackAsync(string providerName, PaymentCallbackRequest request, CancellationToken ct = default);
    Task<AdminResult> RefundBookingAsync(long bookingId, string reason, CancellationToken ct = default);
    IReadOnlyList<PaymentProviderInfo> GetProviders();
}

public interface IPaymentProvider
{
    string Name { get; }
    bool IsSandbox { get; }
    bool SupportsRefund { get; }
    Task<ProviderInitiation> InitiateAsync(ProviderInitiationRequest request, CancellationToken ct = default);
    VerifiedPaymentCallback VerifyCallback(PaymentCallbackRequest request);
    Task<ProviderRefund> RefundAsync(ProviderRefundRequest request, CancellationToken ct = default);
}
