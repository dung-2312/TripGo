using Microsoft.Extensions.Configuration;
using Tripgo.Application.DTOs.Payment;
using Tripgo.Application.Interfaces;

namespace Tripgo.Infrastructure.Services;

/// <summary>
/// Architecture placeholder for the official VNPay gateway integration.
/// In the current phase, VNPay is marked as unintegrated/unavailable.
/// No fake transactions, fake URLs, or simulated confirmations are allowed.
/// Configuration keys (Payments:VnPay:TmnCode, Payments:VnPay:HashSecret, Payments:VnPay:BaseUrl)
/// are prepared for future production/sandbox provisioning without storing hardcoded secrets.
/// </summary>
public sealed class VnPayPaymentProvider : IPaymentProvider
{
    public const string ProviderName = "VNPAY";

    private readonly IConfiguration _configuration;

    public VnPayPaymentProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string Name => ProviderName;
    public string DisplayName => "VNPay (QR / Thẻ nội địa)";
    public bool IsSandbox => bool.TryParse(_configuration["Payments:VnPay:IsSandbox"], out var isSandbox) && isSandbox;

    // Marked unavailable until verified credentials and integration are provisioned.
    public bool IsAvailable => false;
    public bool SupportsRefund => false;

    public Task<ProviderInitiation> InitiateAsync(ProviderInitiationRequest request, CancellationToken ct = default)
    {
        // Must never generate fake transaction URLs or allow initiation when not configured.
        return Task.FromResult(new ProviderInitiation(
            Accepted: false,
            ProviderReference: null,
            RedirectUrl: null,
            Error: "Cổng thanh toán VNPay đang trong quá trình tích hợp và chưa khả dụng."));
    }

    public VerifiedPaymentCallback VerifyCallback(PaymentCallbackRequest request)
    {
        // Unconfigured provider cannot verify or confirm any incoming callback.
        return new VerifiedPaymentCallback
        {
            SignatureValid = false,
            Error = "Cổng thanh toán VNPay chưa được kích hoạt."
        };
    }

    public Task<ProviderRefund> RefundAsync(ProviderRefundRequest request, CancellationToken ct = default)
    {
        return Task.FromResult(new ProviderRefund(
            Accepted: false,
            RefundReference: null,
            Error: "Cổng thanh toán VNPay chưa hỗ trợ hoàn tiền tự động."));
    }
}

