namespace Tripgo.Application.DTOs;

public class BookingPassengerDto
{
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? IdentityCard { get; set; }
    public bool IsPrimaryContact { get; set; } = true;
    public string? SeatCode { get; set; }
}
