using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Tripgo.Application.DTOs.Payment;
using Tripgo.Application.Interfaces;

namespace Tripgo.Infrastructure.Services;

/// <summary>
/// An in-process stand-in for a payment gateway. No money moves. It exercises the same signed-callback
/// path a real provider would, and it can only ever settle bookings flagged as sandbox.
/// </summary>
public sealed class SandboxPaymentProvider : IPaymentProvider
{
    public const string ProviderName = "SANDBOX";
    private static readonly byte[] ProcessKey = RandomNumberGenerator.GetBytes(32);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly byte[] _secret;

    public SandboxPaymentProvider(IConfiguration configuration)
    {
        var configured = configuration["Payments:Sandbox:CallbackSecret"];
        _secret = string.IsNullOrWhiteSpace(configured) ? ProcessKey : Encoding.UTF8.GetBytes(configured);
    }

    public string Name => ProviderName;
    public bool IsSandbox => true;
    public bool SupportsRefund => true;

    public Task<ProviderInitiation> InitiateAsync(ProviderInitiationRequest request, CancellationToken ct = default) =>
        Task.FromResult(new ProviderInitiation(true, $"SBX-{Guid.NewGuid():N}", null, null));

    public Task<ProviderRefund> RefundAsync(ProviderRefundRequest request, CancellationToken ct = default) =>
        Task.FromResult(new ProviderRefund(true, $"SBX-REFUND-{Guid.NewGuid():N}", null));

    public VerifiedPaymentCallback VerifyCallback(PaymentCallbackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsSignatureValid(request.Body, request.Signature))
        {
            return new VerifiedPaymentCallback { SignatureValid = false, Error = "Chữ ký không hợp lệ." };
        }

        SandboxCallbackBody? body;
        try
        {
            body = JsonSerializer.Deserialize<SandboxCallbackBody>(request.Body, JsonOptions);
        }
        catch (JsonException)
        {
            body = null;
        }

        if (body == null)
        {
            return new VerifiedPaymentCallback { SignatureValid = false, Error = "Nội dung không đọc được." };
        }

        return new VerifiedPaymentCallback
        {
            SignatureValid = true,
            EventId = body.EventId,
            TransactionCode = body.TransactionCode,
            ProviderReference = body.ProviderReference,
            Amount = body.Amount,
            Currency = body.Currency,
            State = body.Status switch
            {
                "succeeded" => ProviderPaymentState.Succeeded,
                "failed" => ProviderPaymentState.Failed,
                _ => ProviderPaymentState.Unknown
            }
        };
    }

    /// <summary>Plays the part of the gateway: produces the signed callback it would send.</summary>
    public PaymentCallbackRequest CreateSignedCallback(string eventId, string transactionCode, decimal amount, string currency, string status)
    {
        var body = JsonSerializer.Serialize(new SandboxCallbackBody
        {
            EventId = eventId,
            TransactionCode = transactionCode,
            ProviderReference = $"SBX-{transactionCode}",
            Amount = amount,
            Currency = currency,
            Status = status
        }, JsonOptions);
        return new PaymentCallbackRequest(body, Sign(body));
    }

    private bool IsSignatureValid(string body, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrEmpty(body))
        {
            return false;
        }

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(signature.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(provided, HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(body)));
    }

    private string Sign(string body) =>
        Convert.ToHexString(HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    private sealed class SandboxCallbackBody
    {
        public string? EventId { get; set; }
        public string? TransactionCode { get; set; }
        public string? ProviderReference { get; set; }
        public decimal? Amount { get; set; }
        public string? Currency { get; set; }
        public string? Status { get; set; }
    }
}
