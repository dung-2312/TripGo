using System;

namespace Tripgo.Application.DTOs;

public class TripSearchRequestDto
{
    public long OriginLocationId { get; set; }
    public long DestinationLocationId { get; set; }
    public DateOnly DepartureDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public TripSearchRequestDto() { }
    public TripSearchRequestDto(long originLocationId, long destinationLocationId, DateOnly departureDate)
    {
        OriginLocationId = originLocationId;
        DestinationLocationId = destinationLocationId;
        DepartureDate = departureDate;
    }
}
