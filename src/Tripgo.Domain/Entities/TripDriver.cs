using Tripgo.Domain.Enums;

namespace Tripgo.Domain.Entities;

public class TripDriver
{
    public long TripId { get; set; }

    public long DriverId { get; set; }

    public DriverAssignmentRole Role { get; set; }
        = DriverAssignmentRole.Primary;

    public Trip Trip { get; set; } = null!;

    public Driver Driver { get; set; } = null!;
}