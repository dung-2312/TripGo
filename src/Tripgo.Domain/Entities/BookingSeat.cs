namespace Tripgo.Domain.Entities;

public class BookingSeat
{
    public long Id { get; set; }

    public long BookingId { get; set; }

    public long TripSeatId { get; set; }

    public string SeatCode { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public bool IsActive { get; set; } = true;

    public Booking Booking { get; set; } = null!;

    public TripSeat TripSeat { get; set; } = null!;
}
