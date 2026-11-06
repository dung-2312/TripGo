using Microsoft.Extensions.Configuration;
using Tripgo.Application.DTOs.Payment;
using Tripgo.Application.Interfaces;

namespace Tripgo.Infrastructure.Services;

/// <summary>
/// Architecture placeholder for the official MoMo gateway integration.
/// In the current phase, MoMo is marked as unintegrated/unavailable.
/// No fake transactions, fake QR codes, or simulated confirmations are allowed.
/// Configuration keys (Payments:Momo:PartnerCode, Payments:Momo:AccessKey, Payments:Momo:SecretKey)
/// are prepared for future official integration without storing credentials in source.
/// </summary>
public sealed class MomoPaymentProvider : IPaymentProvider
{
    public const string ProviderName = "MOMO";

    private readonly IConfiguration _configuration;

    public MomoPaymentProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string Name => ProviderName;
    public string DisplayName => "Ví điện tử MoMo";
    public bool IsSandbox => bool.TryParse(_configuration["Payments:Momo:IsSandbox"], out var isSandbox) && isSandbox;

    // Marked unavailable until verified credentials and integration are provisioned.
    public bool IsAvailable => false;
    public bool SupportsRefund => false;

    public Task<ProviderInitiation> InitiateAsync(ProviderInitiationRequest request, CancellationToken ct = default)
    {
        // Must never generate fake transactions or allow initiation when not configured.
        return Task.FromResult(new ProviderInitiation(
            Accepted: false,
            ProviderReference: null,
            RedirectUrl: null,
            Error: "Cổng thanh toán MoMo đang trong quá trình tích hợp và chưa khả dụng."));
    }

    public VerifiedPaymentCallback VerifyCallback(PaymentCallbackRequest request)
    {
        return new VerifiedPaymentCallback
        {
            SignatureValid = false,
            Error = "Cổng thanh toán MoMo chưa được kích hoạt."
        };
    }

    public Task<ProviderRefund> RefundAsync(ProviderRefundRequest request, CancellationToken ct = default)
    {
        return Task.FromResult(new ProviderRefund(
            Accepted: false,
            RefundReference: null,
            Error: "Cổng thanh toán MoMo chưa hỗ trợ hoàn tiền tự động."));
    }
}

