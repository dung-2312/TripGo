using System.Collections.Generic;

namespace Tripgo.Application.DTOs;

public class CreateBookingRequestDto
{
    public long TripId { get; set; }
    public string HoldToken { get; set; } = string.Empty;
    public List<string> SeatCodes { get; set; } = new();
    public long? PickupPointId { get; set; }
    public long? DropoffPointId { get; set; }
    public string PassengerName { get; set; } = string.Empty;
    public string PassengerPhone { get; set; } = string.Empty;
    public string? PassengerEmail { get; set; }
    public string? OwnerIdentifier { get; set; }
    public bool HasInsurance { get; set; }
}
