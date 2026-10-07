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

    public LocationDto() { }
    public LocationDto(long id, string name, string? province)
    {
        Id = id;
        Name = name;
        Province = province;
    }
}

public class OperatorDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Hotline { get; set; }
    public string? Address { get; set; }

    public OperatorDto() { }
    public OperatorDto(long id, string name)
    {
        Id = id;
        Name = name;
    }
}

public class CreateOperatorDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Hotline { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;

    public CreateOperatorDto() { }
}

public class CreateBusDto
{
    public long OperatorId { get; set; }
    public long BusTypeId { get; set; }
    public string LicensePlate { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int TotalSeats { get; set; } = 24;

    public CreateBusDto() { }
}

public class BusTypeDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public BusTypeDto() { }
}

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

public class SeatLayoutItemDto
{
    public long Id { get; set; }
    public string SeatCode { get; set; } = string.Empty;
    public int Floor { get; set; }
    public int Row { get; set; }
    public int Column { get; set; }
    public string SeatType { get; set; } = string.Empty;
    public bool IsAvailable { get; set; } = true;

    public SeatLayoutItemDto() { }
    public SeatLayoutItemDto(long id, string seatCode, int floor, int row, int column, string? seatType, bool isAvailable = true)
    {
        Id = id;
        SeatCode = seatCode;
        Floor = floor;
        Row = row;
        Column = column;
        SeatType = seatType ?? string.Empty;
        IsAvailable = isAvailable;
    }
}

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
    public string DurationText { get; set; } = "12h15m";
    public double Rating { get; set; } = 4.8;
    public int ReviewCount { get; set; } = 250;

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

public class PickupDropoffPointDto
{
    public long Id { get; set; }
    public string Time { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool IsPickup { get; set; }
}

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

public class CustomerBookingInfoDto
{
    public long TripId { get; set; }
    public string BookingCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FullName { get => Name; set => Name = value; }
    public string Phone { get; set; } = string.Empty;
    public string PhoneNumber { get => Phone; set => Phone = value; }
    public string? Email { get; set; }
    public string? PickupPoint { get; set; }
    public string SelectedPickupPoint { get; set; } = string.Empty;
    public string SelectedDropoffPoint { get; set; } = string.Empty;
    public bool HasInsurance { get; set; } = false;
    public List<string> SeatCodes { get; set; } = new();
    public string RouteName { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string BusTypeName { get; set; } = string.Empty;
    public string BusLicensePlate { get; set; } = string.Empty;
    public DateTimeOffset DepartureTime { get; set; }
    public DateTimeOffset BookedAt { get; set; }
    public decimal BasePrice { get; set; }
    public decimal InsuranceAmount { get; set; }

    public CustomerBookingInfoDto() { }
    public CustomerBookingInfoDto(string name, string phone, string? email = null, string? pickupPoint = null)
    {
        Name = name;
        Phone = phone;
        Email = email;
        PickupPoint = pickupPoint;
    }
}

public class SupportCallbackRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }

    public SupportCallbackRequestDto() { }
}

public interface ITripService
{
    Task<List<LocationDto>> GetLocationsAsync(CancellationToken cancellationToken = default);
    Task<long> CreateOperatorAsync(CreateOperatorDto dto, CancellationToken ct = default);
    Task<long> CreateBusWithLayoutAsync(CreateBusDto dto, CancellationToken ct = default);
    Task<List<BusTypeDto>> GetBusTypesAsync(CancellationToken ct = default);
    Task<List<TripSearchResultDto>> SearchTripsAsync(TripSearchRequestDto request, CancellationToken cancellationToken = default);
    Task<TripDetailDto?> GetTripDetailsAsync(long tripId, CancellationToken cancellationToken = default);
    Task<List<OperatorDto>> GetOperatorsAsync(CancellationToken ct = default);
    Task<List<BusSelectDto>> GetBusesByOperatorAsync(long operatorId, CancellationToken ct = default);
    Task<List<RouteDto>> GetRoutesAsync(CancellationToken ct = default);
    Task<long> CreateRouteAsync(CreateRouteDto dto, CancellationToken ct = default);
    Task<long> CreateTripAsync(CreateTripDto dto, CancellationToken ct = default);
    Task<bool> DeleteTripAsync(long tripId, CancellationToken ct = default);
    Task<List<TripSearchResultDto>> GetAllTripsAdminAsync(CancellationToken ct = default);
    Task<bool> BookSeatsAsync(long tripId, List<string> seatCodes, CustomerBookingInfoDto customerInfo, CancellationToken ct = default);
    Task<List<CustomerBookingInfoDto>> GetBookingsByPhoneAsync(string phone, CancellationToken ct = default);
    Task<CustomerBookingInfoDto?> GetBookingByCodeAsync(string bookingCode, CancellationToken ct = default);
    Task<bool> SubmitSupportCallbackAsync(string name, string phone, CancellationToken ct = default);
}