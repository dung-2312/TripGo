using System;
using System.Collections.Generic;

namespace Tripgo.Application.DTOs;

public class SeatHoldResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? HoldToken { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public List<string> HeldSeatCodes { get; set; } = new();
    public decimal TotalPrice { get; set; }
}
