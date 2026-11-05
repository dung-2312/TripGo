namespace Tripgo.Application.DTOs;

public class CreateBusDto
{
    public long OperatorId { get; set; }
    public long BusTypeId { get; set; }
    public string LicensePlate { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SeatLayoutDefinition { get; set; } = string.Empty;

    public CreateBusDto() { }
}
