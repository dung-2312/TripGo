namespace Tripgo.Application.DTOs;

public class PickupDropoffPointDto
{
    public long Id { get; set; }
    public string Time { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool IsPickup { get; set; }
}
