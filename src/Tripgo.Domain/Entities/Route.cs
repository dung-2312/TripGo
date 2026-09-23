namespace Tripgo.Domain.Entities;

public class Route
{
    public long Id { get; set; }

    public long OperatorId { get; set; }

    public long OriginLocationId { get; set; }

    public long DestinationLocationId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public decimal? DistanceKm { get; set; }

    public int? EstimatedDurationMinutes { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
        = DateTimeOffset.UtcNow;

    public Operator Operator { get; set; } = null!;

    public Location OriginLocation { get; set; } = null!;

    public Location DestinationLocation { get; set; } = null!;

    public ICollection<RouteStop> Stops { get; set; }
        = new List<RouteStop>();

    public ICollection<Trip> Trips { get; set; }
        = new List<Trip>();
}