namespace Tripgo.Application.DTOs;

public class BusSelectDto
{
    public long Id { get; set; }
    public long OperatorId { get; set; }
    public long BusTypeId { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string LicensePlate { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BusTypeName { get; set; } = string.Empty;
    public int TotalSeats { get; set; }

    public BusSelectDto() { }
    public BusSelectDto(long id, string licensePlate, string busTypeName, int totalSeats = 0)
    {
        Id = id;
        LicensePlate = licensePlate;
        BusTypeName = busTypeName;
        TotalSeats = totalSeats;
    }
}
