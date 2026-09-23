namespace Tripgo.Domain.Enums;

[Flags]
public enum PointType
{
    Pickup = 1,
    Dropoff = 2,
    Both = Pickup | Dropoff
}