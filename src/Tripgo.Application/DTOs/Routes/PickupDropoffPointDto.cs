namespace Tripgo.Application.DTOs;

public class PickupDropoffPointDto
{
    public const string UnknownTimeText = "Giờ đón/trả theo điều phối";

    public long Id { get; set; }
    public string Time { get; set; } = UnknownTimeText;
    public bool HasFixedTime { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool IsPickup { get; set; }
}
