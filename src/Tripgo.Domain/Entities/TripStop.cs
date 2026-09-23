namespace Tripgo.Domain.Entities;

public class TripStop
{
    public long Id { get; set; }

    public long TripId { get; set; }

    public long PointId { get; set; }

    public int StopOrder { get; set; }

    public DateTimeOffset? ScheduledArrivalTime { get; set; }

    public DateTimeOffset? ScheduledDepartureTime { get; set; }

    public bool AllowPickup { get; set; }

    public bool AllowDropoff { get; set; }

    public Trip Trip { get; set; } = null!;

    public PickupDropoffPoint Point { get; set; } = null!;
}