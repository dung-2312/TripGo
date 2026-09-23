using Tripgo.Domain.Enums;

namespace Tripgo.Domain.Entities;

public class PickupDropoffPoint
{
    public long Id { get; set; }

    public long OperatorId { get; set; }

    public long LocationId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public PointType PointType { get; set; } = PointType.Both;

    public bool IsActive { get; set; } = true;

    public Operator Operator { get; set; } = null!;

    public Location Location { get; set; } = null!;
}