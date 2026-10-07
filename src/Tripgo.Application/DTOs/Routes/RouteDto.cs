namespace Tripgo.Application.DTOs;

public class RouteDto
{
    public long Id { get; set; }
    public long OperatorId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long OriginLocationId { get; set; }
    public long DestinationLocationId { get; set; }
    public string OriginName { get; set; } = string.Empty;
    public string DestinationName { get; set; } = string.Empty;
    public decimal? DistanceKm { get; set; }
    public int? EstimatedDurationMinutes { get; set; }

    public RouteDto() { }
    public RouteDto(long id, string name, long originLocationId, long destinationLocationId, string originName = "", string destinationName = "")
    {
        Id = id;
        Name = name;
        OriginLocationId = originLocationId;
        DestinationLocationId = destinationLocationId;
        OriginName = originName;
        DestinationName = destinationName;
    }
}
