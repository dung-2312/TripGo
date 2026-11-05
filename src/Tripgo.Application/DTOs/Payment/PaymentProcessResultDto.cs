using System;
using Tripgo.Domain.Enums;

namespace Tripgo.Application.DTOs;

public class PaymentProcessResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? TransactionCode { get; set; }
    public string? BookingCode { get; set; }
    public PaymentStatus Status { get; set; }
    public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsSandbox { get; set; } = true;
}
