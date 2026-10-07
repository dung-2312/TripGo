namespace Tripgo.Application.DTOs;

public class CreateRouteDto
{
    public long OperatorId { get; set; }
    public long OriginLocationId { get; set; }
    public long DestinationLocationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal DistanceKm { get; set; }
    public int EstimatedDurationMinutes { get; set; }
}
