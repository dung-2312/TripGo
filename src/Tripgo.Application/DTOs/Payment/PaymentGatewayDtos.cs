namespace Tripgo.Application.DTOs.Payment;

public sealed record PaymentProviderInfo(string Name, bool IsSandbox, bool SupportsRefund);

public sealed class PaymentInitiationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? TransactionCode { get; init; }
    public string? RedirectUrl { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "VND";
    public bool IsSandbox { get; init; }
}

public sealed class SandboxPaymentResult
{
    public bool Success { get; init; }
    public bool PaymentSucceeded { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? BookingCode { get; init; }
    public string? TransactionCode { get; init; }
    public string? Outcome { get; init; }
}

/// <summary>TransactionCode is also the stable idempotency key for provider retries.</summary>
public sealed record ProviderInitiationRequest(string TransactionCode, string BookingCode, decimal Amount, string Currency, DateTimeOffset ExpiresAt);
public sealed record ProviderInitiation(bool Accepted, string? ProviderReference, string? RedirectUrl, string? Error);
public sealed record ProviderRefundRequest(string TransactionCode, string? ProviderReference, decimal Amount, string Currency, string Reason);
public sealed record ProviderRefund(bool Accepted, string? RefundReference, string? Error);

/// <summary>The untouched callback as received: verification must see the exact bytes that were signed.</summary>
public sealed record PaymentCallbackRequest(string Body, string? Signature);

public enum ProviderPaymentState
{
    Unknown = 0,
    Succeeded = 1,
    Failed = 2
}

public sealed class VerifiedPaymentCallback
{
    public bool SignatureValid { get; init; }
    public string? EventId { get; init; }
    public string? TransactionCode { get; init; }
    public string? ProviderReference { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public ProviderPaymentState State { get; init; }
    public string? Error { get; init; }
}

public sealed class PaymentCallbackOutcome
{
    public string Outcome { get; init; } = string.Empty;
    public bool Accepted { get; init; }
    public string? BookingCode { get; init; }
}
