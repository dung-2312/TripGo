using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Tripgo.Domain.Interfaces;

public class LocationDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Province { get; set; }
    public string? Country { get; set; }
    public bool IsActive { get; set; }
}

public class SeatLayoutItemDto
{
    public long Id { get; set; }
    public string SeatCode { get; set; } = string.Empty;
    public int Floor { get; set; }
    public int Row { get; set; }
    public int Column { get; set; }
    public string SeatType { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
}

public class TripSearchResultDto
{
    public long Id { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string BusTypeName { get; set; } = string.Empty;
    public DateTimeOffset DepartureTime { get; set; }
    public DateTimeOffset ArrivalTime { get; set; }
    public decimal BasePrice { get; set; }
    public int AvailableSeats { get; set; }
    public int TotalSeats { get; set; }
}

public class TripDetailDto
{
    public long Id { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string BusTypeName { get; set; } = string.Empty;
    public string BusLicensePlate { get; set; } = string.Empty;
    public DateTimeOffset DepartureTime { get; set; }
    public DateTimeOffset ArrivalTime { get; set; }
    public decimal BasePrice { get; set; }
    public int TotalSeats { get; set; }
    public List<SeatLayoutItemDto> Seats { get; set; } = new();
}

public class TripSearchRequestDto
{
    public long OriginLocationId { get; set; }
    public long DestinationLocationId { get; set; }
    public DateOnly DepartureDate { get; set; }
}

public interface ITripService
{
    Task<List<LocationDto>> GetLocationsAsync(CancellationToken cancellationToken = default);
    Task<List<TripSearchResultDto>> SearchTripsAsync(TripSearchRequestDto request, CancellationToken cancellationToken = default);
    Task<TripDetailDto?> GetTripDetailsAsync(long tripId, CancellationToken cancellationToken = default);
}