using System.Collections.Generic;

namespace Tripgo.Application.DTOs;

public class SeatHoldRequestDto
{
    public long TripId { get; set; }
    public List<string> SeatCodes { get; set; } = new();
    public string? ExistingHoldToken { get; set; }
    public string? OwnerIdentifier { get; set; }
    public int HoldMinutes { get; set; } = 10;
}
