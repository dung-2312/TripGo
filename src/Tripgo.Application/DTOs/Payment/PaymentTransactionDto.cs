using System;

namespace Tripgo.Application.DTOs;

public class PaymentTransactionDto
{
    public long Id { get; set; }
    public long BookingId { get; set; }
    public string TransactionCode { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProviderReference { get; set; }
    public string? Note { get; set; }
    public bool IsSandbox { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
