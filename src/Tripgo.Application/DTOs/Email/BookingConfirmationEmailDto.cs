namespace Tripgo.Application.DTOs;

public sealed class BookingConfirmationEmailDto
{
    public string BookingCode { get; set; } = string.Empty;
    public string PassengerName { get; set; } = string.Empty;
    public string RecipientEmail { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public DateTimeOffset DepartureTime { get; set; }
    public string PickupPoint { get; set; } = string.Empty;
    public string DropoffPoint { get; set; } = string.Empty;
    public List<string> SeatCodes { get; set; } = new();
    public decimal TotalAmount { get; set; }
}
