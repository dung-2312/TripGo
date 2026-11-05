namespace Tripgo.Domain.Entities;

/// <summary>Every provider callback received, valid or not. The reconciliation record of what providers told us.</summary>
public class PaymentWebhookEvent
{
    public const string OutcomeApplied = "Applied";
    public const string OutcomeDuplicate = "Duplicate";
    public const string OutcomeInvalidSignature = "InvalidSignature";
    public const string OutcomeUnknownTransaction = "UnknownTransaction";
    public const string OutcomeAmountMismatch = "AmountMismatch";
    public const string OutcomeCurrencyMismatch = "CurrencyMismatch";
    public const string OutcomeLatePayment = "LatePayment";
    public const string OutcomeIgnored = "Ignored";

    public long Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? EventId { get; set; }
    public string? TransactionCode { get; set; }
    public bool SignatureValid { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public decimal? ReportedAmount { get; set; }
    public string? ReportedCurrency { get; set; }
    public string? Detail { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
}
