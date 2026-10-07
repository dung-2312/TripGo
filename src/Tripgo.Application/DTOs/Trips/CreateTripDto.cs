using System;

namespace Tripgo.Application.DTOs;

public class CreateTripDto
{
    public long RouteId { get; set; }
    public long BusId { get; set; }
    public long OperatorId { get; set; }
    public DateTimeOffset DepartureTime { get; set; }
    public DateTimeOffset ExpectedArrivalTime { get; set; }
    public decimal BasePrice { get; set; }

    public CreateTripDto() { }
    public CreateTripDto(long routeId, long busId, long operatorId, DateTimeOffset departureTime, DateTimeOffset expectedArrivalTime, decimal basePrice)
    {
        RouteId = routeId;
        BusId = busId;
        OperatorId = operatorId;
        DepartureTime = departureTime;
        ExpectedArrivalTime = expectedArrivalTime;
        BasePrice = basePrice;
    }
}
