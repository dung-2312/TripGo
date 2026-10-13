using System;

namespace Tripgo.Application.DTOs;

public class BookingResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? BookingCode { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public decimal TotalAmount { get; set; }
    public bool IsDemoBooking { get; set; } = true;
}
