using System;
using System.Collections.Generic;

namespace Tripgo.Application.DTOs;

public class TripDetailDto
{
    public long Id { get; set; }
    public string TripCode { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string BusTypeName { get; set; } = string.Empty;
    public string BusLicensePlate { get; set; } = string.Empty;
    public DateTimeOffset DepartureTime { get; set; }
    public DateTimeOffset ArrivalTime { get; set; }
    public decimal BasePrice { get; set; }
    public int AvailableSeats { get; set; }
    public int TotalSeats { get; set; }
    public List<SeatLayoutItemDto> Seats { get; set; } = new();
    public List<PickupDropoffPointDto> PickupPoints { get; set; } = new();
    public List<PickupDropoffPointDto> DropoffPoints { get; set; } = new();
    public bool IsBookable { get; set; } = false;
    public string OperationalTier { get; set; } = "PUBLISHED_REFERENCE_SCHEDULE";
    public string OperationalTierLabel { get; set; } = "Lịch trình tham khảo";
    public string OperatorHotline { get; set; } = string.Empty;
    public string DurationText { get; set; } = string.Empty;
    public double Rating { get; set; }
    public int ReviewCount { get; set; }

    public TripDetailDto() { }
    public TripDetailDto(long id, string tripCode, string routeName, string operatorName, string busLicensePlate, DateTimeOffset departureTime, DateTimeOffset arrivalTime, decimal basePrice, int availableSeats, List<SeatLayoutItemDto> seats, string busTypeName = "")
    {
        Id = id;
        TripCode = tripCode;
        RouteName = routeName;
        OperatorName = operatorName;
        BusLicensePlate = busLicensePlate;
        DepartureTime = departureTime;
        ArrivalTime = arrivalTime;
        BasePrice = basePrice;
        AvailableSeats = availableSeats;
        Seats = seats;
        BusTypeName = busTypeName;
        TotalSeats = seats.Count;
    }
}
