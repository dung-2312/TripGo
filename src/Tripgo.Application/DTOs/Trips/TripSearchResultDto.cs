using System;

namespace Tripgo.Application.DTOs;

public class TripSearchResultDto
{
    public long Id { get; set; }
    public string TripCode { get; set; } = string.Empty;
    public string BusLicensePlate { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string BusTypeName { get; set; } = string.Empty;
    public DateTimeOffset DepartureTime { get; set; }
    public DateTimeOffset ArrivalTime { get; set; }
    public decimal BasePrice { get; set; }
    public int AvailableSeats { get; set; }
    public int TotalSeats { get; set; }
    public List<PickupDropoffPointDto> PickupPoints { get; set; } = new();
    public List<PickupDropoffPointDto> DropoffPoints { get; set; } = new();
    public bool IsBookable { get; set; } = false;
    public string OperationalTier { get; set; } = "PUBLISHED_REFERENCE_SCHEDULE";
    public string OperationalTierLabel { get; set; } = "Lịch trình tham khảo";
    public string OperatorHotline { get; set; } = string.Empty;

    public TripSearchResultDto() { }
    public TripSearchResultDto(long id, string tripCode, string routeName, string operatorName, string busTypeName, DateTimeOffset departureTime, decimal basePrice, int availableSeats, int totalSeats, DateTimeOffset? arrivalTime = null)
    {
        Id = id;
        TripCode = tripCode;
        RouteName = routeName;
        OperatorName = operatorName;
        BusTypeName = busTypeName;
        DepartureTime = departureTime;
        ArrivalTime = arrivalTime ?? departureTime.AddHours(4);
        BasePrice = basePrice;
        AvailableSeats = availableSeats;
        TotalSeats = totalSeats;
    }
}
