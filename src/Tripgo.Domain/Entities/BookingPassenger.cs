namespace Tripgo.Domain.Entities;

public class BookingPassenger
{
    public long Id { get; set; }

    public long BookingId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? IdentityCard { get; set; }

    public bool IsPrimaryContact { get; set; } = true;

    public Booking Booking { get; set; } = null!;
}
