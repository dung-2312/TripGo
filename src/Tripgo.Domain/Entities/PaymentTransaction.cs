using System;
using Tripgo.Domain.Enums;

namespace Tripgo.Domain.Entities;

public class PaymentTransaction
{
    public long Id { get; set; }

    public long BookingId { get; set; }

    public string TransactionCode { get; set; } = string.Empty;

    public string PaymentMethod { get; set; } = "SANDBOX_SIMULATED";

    public decimal Amount { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    public string? ProviderReference { get; set; }

    public string? Note { get; set; }

    public bool IsSandbox { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }

    public Booking Booking { get; set; } = null!;
}
