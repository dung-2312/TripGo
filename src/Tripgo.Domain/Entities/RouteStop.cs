namespace Tripgo.Domain.Entities;

public class RouteStop
{
    public long Id { get; set; }

    public long RouteId { get; set; }

    public long PointId { get; set; }

    public int StopOrder { get; set; }

    public int? ArrivalOffsetMinutes { get; set; }

    public int? DepartureOffsetMinutes { get; set; }

    public bool AllowPickup { get; set; }

    public bool AllowDropoff { get; set; }

    public Route Route { get; set; } = null!;

    public PickupDropoffPoint Point { get; set; } = null!;
}