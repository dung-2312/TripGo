using Tripgo.Domain.Enums;

namespace Tripgo.Domain.Entities;

public class Trip
{
    public long Id { get; set; }

    public long RouteId { get; set; }

    public long BusId { get; set; }

    public string TripCode { get; set; } = string.Empty;

    public DateTimeOffset DepartureTime { get; set; }

    public DateTimeOffset ExpectedArrivalTime { get; set; }

    public decimal BasePrice { get; set; }

    public TripStatus Status { get; set; }
        = TripStatus.Draft;

    public bool IsDemoTrip { get; set; } = false;

    public DateTimeOffset CreatedAt { get; set; }
        = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    public Route Route { get; set; } = null!;

    public Bus Bus { get; set; } = null!;

    public ICollection<TripStop> Stops { get; set; }
        = new List<TripStop>();

    public ICollection<TripDriver> Drivers { get; set; }
        = new List<TripDriver>();

    public ICollection<TripSeat> Seats { get; set; }
        = new List<TripSeat>();

    public ICollection<Booking> Bookings { get; set; }
        = new List<Booking>();
}