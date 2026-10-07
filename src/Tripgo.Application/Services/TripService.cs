using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tripgo.Application.DTOs;
using Tripgo.Application.Interfaces;
using Tripgo.Application.Interfaces.Persistence;
using Tripgo.Application.Services.Temporary;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;

namespace Tripgo.Application.Services;

public class TripService : ITripService
{
    private readonly ITripRepository _repository;
    private readonly ITemporaryBookingStore _bookingStore;

    public TripService(ITripRepository repository, ITemporaryBookingStore bookingStore)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _bookingStore = bookingStore ?? throw new ArgumentNullException(nameof(bookingStore));
    }

    public async Task<List<LocationDto>> GetLocationsAsync(CancellationToken cancellationToken = default)
    {
        var locations = await _repository.GetActiveLocationsAsync(cancellationToken);
        return locations.Select(l => new LocationDto
        {
            Id = l.Id,
            Code = l.Code,
            Name = l.Name,
            Province = l.Province,
            Country = l.Country,
            IsActive = l.IsActive
        }).ToList();
    }

    public async Task<long> CreateOperatorAsync(CreateOperatorDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("Mã và tên nhà xe là bắt buộc.", nameof(dto));

        var operatorEntity = new Operator
        {
            Code = dto.Code.Trim(),
            Name = dto.Name.Trim(),
            Phone = string.IsNullOrWhiteSpace(dto.Hotline) ? null : dto.Hotline.Trim(),
            Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim(),
            IsActive = true,
            IsVerified = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _repository.AddOperatorAsync(operatorEntity, ct);
        return operatorEntity.Id;
    }

    public async Task<long> CreateBusWithLayoutAsync(CreateBusDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (dto.OperatorId <= 0 || dto.BusTypeId <= 0 || string.IsNullOrWhiteSpace(dto.LicensePlate))
            throw new ArgumentException("Nhà xe, loại xe và biển số là bắt buộc.", nameof(dto));

        var hasOperator = await _repository.OperatorExistsAndActiveAsync(dto.OperatorId, ct);
        var busType = await _repository.GetActiveBusTypeByIdAsync(dto.BusTypeId, ct);
        if (!hasOperator || busType == null)
            throw new InvalidOperationException("Nhà xe hoặc loại xe không còn hoạt động.");

        var busTypeName = busType.Name;
        var isCoupleSuite = busTypeName.Contains("đôi", StringComparison.OrdinalIgnoreCase) ||
                            busTypeName.Contains("double", StringComparison.OrdinalIgnoreCase);
        var seatCount = isCoupleSuite ? 16 : 24;

        var layout = new SeatLayout
        {
            Name = $"Sơ đồ xe {dto.LicensePlate.Trim()}",
            Floors = 2,
            TotalSeats = seatCount,
            IsActive = true,
            Items = Enumerable.Range(1, seatCount).Select(index =>
            {
                var seatsPerFloor = seatCount / 2;
                var floor = index <= seatsPerFloor ? 1 : 2;
                var position = ((index - 1) % seatsPerFloor) + 1;
                var prefix = isCoupleSuite ? 'P' : floor == 1 ? 'A' : 'B';
                return new SeatLayoutItem
                {
                    SeatCode = $"{prefix}{position:D2}",
                    Floor = floor,
                    Row = (position - 1) / 2 + 1,
                    Column = (position - 1) % 2 + 1,
                    SeatType = isCoupleSuite ? "DoubleBed" : "Bed",
                    IsActive = true
                };
            }).ToList()
        };

        var bus = new Bus
        {
            OperatorId = dto.OperatorId,
            BusTypeId = dto.BusTypeId,
            LicensePlate = dto.LicensePlate.Trim(),
            Name = string.IsNullOrWhiteSpace(dto.Name) ? dto.LicensePlate.Trim() : dto.Name.Trim(),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            SeatLayout = layout
        };

        await _repository.AddBusAsync(bus, ct);
        return bus.Id;
    }

    public async Task<List<BusTypeDto>> GetBusTypesAsync(CancellationToken ct = default)
    {
        var busTypes = await _repository.GetActiveBusTypesAsync(ct);
        return busTypes.Select(type => new BusTypeDto
        {
            Id = type.Id,
            Name = type.Name,
            Description = type.Description ?? string.Empty
        }).ToList();
    }

    public async Task<List<TripSearchResultDto>> SearchTripsAsync(TripSearchRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var requestedDate = request.DepartureDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
        var start = new DateTimeOffset(requestedDate);
        var end = start.AddDays(1).AddTicks(-1);
        var windowStart = start.AddDays(-3);
        var windowEnd = end.AddDays(3);

        var matchingTrips = await _repository.SearchTripsAsync(
            request.OriginLocationId,
            request.DestinationLocationId,
            windowStart,
            windowEnd,
            cancellationToken);

        var exactDateTrips = matchingTrips
            .Where(trip => trip.DepartureTime.ToLocalTime().Date == request.DepartureDate.ToDateTime(TimeOnly.MinValue).Date)
            .ToList();

        var trips = exactDateTrips.Count > 0
            ? exactDateTrips
            : matchingTrips
                .OrderBy(trip => Math.Abs((trip.DepartureTime.ToLocalTime().Date - request.DepartureDate.ToDateTime(TimeOnly.MinValue).Date).Days))
                .ThenBy(trip => trip.DepartureTime)
                .ToList();

        return trips.Select(t =>
        {
            var totalSeats = t.Bus?.SeatLayout?.TotalSeats ?? 0;
            var bookedCount = _bookingStore.GetBookedSeatCount(t.Id);

            return new TripSearchResultDto
            {
                Id = t.Id,
                TripCode = t.TripCode,
                BusLicensePlate = t.Bus?.LicensePlate ?? string.Empty,
                Status = t.Status.ToString(),
                RouteName = t.Route?.Name ?? string.Empty,
                OperatorName = t.Route?.Operator?.Name ?? string.Empty,
                BusTypeName = t.Bus?.BusType?.Name ?? string.Empty,
                DepartureTime = t.DepartureTime,
                ArrivalTime = t.ExpectedArrivalTime,
                BasePrice = t.BasePrice,
                TotalSeats = totalSeats,
                AvailableSeats = Math.Max(0, totalSeats - bookedCount)
            };
        }).ToList();
    }

    public async Task<TripDetailDto?> GetTripDetailsAsync(long tripId, CancellationToken cancellationToken = default)
    {
        var trip = await _repository.GetTripWithDetailsAsync(tripId, cancellationToken);
        if (trip == null) return null;

        var bookedSet = _bookingStore.GetBookedSeatSnapshot(tripId);
        var seatCodeMap = CreateSeatCodeMap(trip.Bus?.SeatLayout, trip.Bus?.BusType);
        var displayCodesByInternal = seatCodeMap.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

        var seatItems = trip.Bus?.SeatLayout?.Items?
            .OrderBy(i => i.Floor)
            .ThenBy(i => i.Row)
            .ThenBy(i => i.Column)
            .Select(i => new SeatLayoutItemDto
            {
                Id = i.Id,
                SeatCode = displayCodesByInternal.TryGetValue(i.SeatCode, out var displayCode) ? displayCode : i.SeatCode,
                Floor = i.Floor,
                Row = i.Row,
                Column = i.Column,
                SeatType = i.SeatType ?? string.Empty,
                IsAvailable = i.IsActive && !bookedSet.Contains(i.SeatCode)
            })
            .ToList() ?? new List<SeatLayoutItemDto>();

        return new TripDetailDto
        {
            Id = trip.Id,
            TripCode = trip.TripCode,
            RouteName = trip.Route?.Name ?? string.Empty,
            OperatorName = trip.Route?.Operator?.Name ?? string.Empty,
            BusTypeName = trip.Bus?.BusType?.Name ?? string.Empty,
            BusLicensePlate = trip.Bus?.LicensePlate ?? string.Empty,
            DepartureTime = trip.DepartureTime,
            ArrivalTime = trip.ExpectedArrivalTime,
            BasePrice = trip.BasePrice,
            TotalSeats = trip.Bus?.SeatLayout?.TotalSeats ?? 0,
            AvailableSeats = Math.Max(0, (trip.Bus?.SeatLayout?.TotalSeats ?? 0) - bookedSet.Count),
            Seats = seatItems,
            PickupPoints = new List<PickupDropoffPointDto>
            {
                new() { Id = 1, Time = trip.DepartureTime.ToLocalTime().ToString("HH:mm"), Name = "Bến xe Quy Nhơn", Address = "02 Nguyễn Tất Thành, TP. Quy Nhơn", IsPickup = true },
                new() { Id = 2, Time = trip.DepartureTime.AddMinutes(25).ToLocalTime().ToString("HH:mm"), Name = "Văn phòng Diêu Trì", Address = "Ga Diêu Trì, huyện Tuy Phước", IsPickup = true }
            },
            DropoffPoints = new List<PickupDropoffPointDto>
            {
                new() { Id = 3, Time = trip.ExpectedArrivalTime.ToLocalTime().ToString("HH:mm"), Name = "Bến xe Miền Đông", Address = "292 Đinh Bộ Lĩnh, Bình Thạnh, TP. Hồ Chí Minh", IsPickup = false },
                new() { Id = 4, Time = trip.ExpectedArrivalTime.AddMinutes(30).ToLocalTime().ToString("HH:mm"), Name = "Cầu vượt Linh Xuân", Address = "Xa lộ Hà Nội, TP. Thủ Đức", IsPickup = false }
            }
        };
    }

    public async Task<List<OperatorDto>> GetOperatorsAsync(CancellationToken ct = default)
    {
        var operators = await _repository.GetActiveOperatorsAsync(ct);
        return operators.Select(o => new OperatorDto
        {
            Id = o.Id,
            Code = o.Code,
            Name = o.Name,
            Hotline = o.Phone,
            Address = o.Address
        }).ToList();
    }

    public async Task<List<BusSelectDto>> GetBusesByOperatorAsync(long operatorId, CancellationToken ct = default)
    {
        var buses = await _repository.GetActiveBusesByOperatorAsync(operatorId, ct);
        return buses.Select(b => new BusSelectDto
        {
            Id = b.Id,
            OperatorId = b.OperatorId,
            BusTypeId = b.BusTypeId,
            OperatorName = b.Operator != null ? b.Operator.Name : string.Empty,
            LicensePlate = b.LicensePlate,
            Name = b.Name ?? string.Empty,
            BusTypeName = b.BusType != null ? b.BusType.Name : string.Empty,
            TotalSeats = b.SeatLayout != null ? b.SeatLayout.TotalSeats : 0
        }).ToList();
    }

    public async Task<List<RouteDto>> GetRoutesAsync(CancellationToken ct = default)
    {
        var routes = await _repository.GetActiveRoutesAsync(ct);
        return routes.Select(r => new RouteDto
        {
            Id = r.Id,
            Code = r.Code,
            OperatorId = r.OperatorId,
            Name = r.Name,
            OriginLocationId = r.OriginLocationId,
            DestinationLocationId = r.DestinationLocationId,
            OriginName = r.OriginLocation != null ? r.OriginLocation.Name : string.Empty,
            DestinationName = r.DestinationLocation != null ? r.DestinationLocation.Name : string.Empty,
            DistanceKm = r.DistanceKm,
            EstimatedDurationMinutes = r.EstimatedDurationMinutes
        }).ToList();
    }

    public async Task<long> CreateRouteAsync(CreateRouteDto dto, CancellationToken ct = default)
    {
        var route = new Route
        {
            OperatorId = dto.OperatorId,
            OriginLocationId = dto.OriginLocationId,
            DestinationLocationId = dto.DestinationLocationId,
            Code = string.IsNullOrWhiteSpace(dto.Code) ? $"R-{DateTime.UtcNow.Ticks % 10000}" : dto.Code,
            Name = dto.Name,
            DistanceKm = dto.DistanceKm,
            EstimatedDurationMinutes = dto.EstimatedDurationMinutes,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _repository.AddRouteAsync(route, ct);
        return route.Id;
    }

    public async Task<long> CreateTripAsync(CreateTripDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var routeOperatorId = await _repository.GetRouteOperatorIdAsync(dto.RouteId, ct);
        var busOperatorId = await _repository.GetBusOperatorIdAsync(dto.BusId, ct);
        if (routeOperatorId == null || busOperatorId == null || routeOperatorId != busOperatorId)
            throw new InvalidOperationException("Tuyến và xe phải thuộc cùng một nhà xe đang hoạt động.");

        var trip = new Trip
        {
            RouteId = dto.RouteId,
            BusId = dto.BusId,
            TripCode = $"TRIP-{DateTime.UtcNow.Ticks % 100000}",
            DepartureTime = dto.DepartureTime,
            ExpectedArrivalTime = dto.ExpectedArrivalTime,
            BasePrice = dto.BasePrice,
            Status = TripStatus.Scheduled,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _repository.AddTripAsync(trip, ct);
        return trip.Id;
    }

    public async Task<List<TripSearchResultDto>> GetAllTripsAdminAsync(CancellationToken ct = default)
    {
        var trips = await _repository.GetAllTripsAdminAsync(ct);
        return trips.Select(t =>
        {
            var totalSeats = t.Bus?.SeatLayout?.TotalSeats ?? 0;
            var bookedCount = _bookingStore.GetBookedSeatCount(t.Id);

            return new TripSearchResultDto
            {
                Id = t.Id,
                TripCode = t.TripCode,
                BusLicensePlate = t.Bus?.LicensePlate ?? string.Empty,
                Status = t.Status.ToString(),
                RouteName = t.Route?.Name ?? string.Empty,
                OperatorName = t.Route?.Operator?.Name ?? string.Empty,
                BusTypeName = t.Bus?.BusType?.Name ?? string.Empty,
                DepartureTime = t.DepartureTime,
                ArrivalTime = t.ExpectedArrivalTime,
                BasePrice = t.BasePrice,
                TotalSeats = totalSeats,
                AvailableSeats = Math.Max(0, totalSeats - bookedCount)
            };
        }).ToList();
    }

    public async Task<bool> DeleteTripAsync(long tripId, CancellationToken ct = default)
    {
        var cancelled = await _repository.CancelTripAsync(tripId, ct);
        if (!cancelled)
            return false;

        _bookingStore.RemoveTripBookedSeats(tripId);
        return true;
    }

    public async Task<bool> BookSeatsAsync(long tripId, List<string> seatCodes, CustomerBookingInfoDto customerInfo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(customerInfo);
        if (seatCodes == null || seatCodes.Count == 0) return false;
        var requestedCodes = seatCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (requestedCodes.Count != seatCodes.Count) return false;

        var trip = await _repository.GetTripForBookingAsync(tripId, ct);
        if (trip?.Bus?.SeatLayout?.Items == null)
            return false;

        var seatCodeMap = CreateSeatCodeMap(trip.Bus.SeatLayout, trip.Bus.BusType);
        var activeSeatCodes = requestedCodes
            .Select(code => seatCodeMap.TryGetValue(code, out var internalCode) ? internalCode : null)
            .ToList();
        if (activeSeatCodes.Any(string.IsNullOrEmpty))
            return false;
        var internalCodes = activeSeatCodes.Select(code => code!).ToList();

        return _bookingStore.TryBookSeats(
            tripId,
            internalCodes,
            requestedCodes,
            customerInfo,
            trip.Route?.Name ?? string.Empty,
            trip.Route?.Operator?.Name ?? string.Empty,
            trip.Bus.BusType?.Name ?? string.Empty,
            trip.Bus.LicensePlate,
            trip.DepartureTime,
            trip.BasePrice);
    }

    public Task<List<CustomerBookingInfoDto>> GetBookingsByPhoneAsync(string phone, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedPhone = NormalizePhone(phone);
        var bookings = _bookingStore.GetBookingsByPhone(normalizedPhone);
        return Task.FromResult(bookings);
    }

    public Task<CustomerBookingInfoDto?> GetBookingByCodeAsync(string bookingCode, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var booking = _bookingStore.GetBookingByCode(bookingCode);
        return Task.FromResult(booking);
    }

    public Task<bool> SubmitSupportCallbackAsync(string name, string phone, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name) || NormalizePhone(phone).Length < 9)
            return Task.FromResult(false);

        _bookingStore.EnqueueSupportCallback(new SupportCallbackRequestDto
        {
            Name = name.Trim(),
            Phone = phone.Trim(),
            RequestedAt = DateTimeOffset.UtcNow
        });
        return Task.FromResult(true);
    }

    private static Dictionary<string, string> CreateSeatCodeMap(SeatLayout? layout, BusType? busType)
    {
        var seatMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (layout?.Items == null || layout.Items.Count == 0)
            return seatMap;

        var floors = Math.Max(1, layout.Floors);
        var seatsPerFloor = Math.Max(1, layout.TotalSeats / floors);
        var isCoupleSuite = busType?.Name.Contains("đôi", StringComparison.OrdinalIgnoreCase) == true ||
                            layout.Items.Any(seat => string.Equals(seat.SeatType, "DoubleBed", StringComparison.OrdinalIgnoreCase));

        foreach (var seat in layout.Items.Where(item => item.IsActive))
        {
            var floorIndex = Math.Clamp(seat.Floor - 1, 0, floors - 1);
            var positionOnFloor = (seat.Row - 1) * 2 + seat.Column;
            if (positionOnFloor < 1)
                positionOnFloor = seatMap.Count % seatsPerFloor + 1;

            var displayCode = isCoupleSuite
                ? $"P{(floorIndex * seatsPerFloor) + positionOnFloor:D2}"
                : $"{(floorIndex == 0 ? 'A' : 'B')}{positionOnFloor:D2}";
            seatMap[displayCode] = seat.SeatCode;
        }

        return seatMap;
    }

    private static string NormalizePhone(string? phone) =>
        new((phone ?? string.Empty).Where(char.IsDigit).ToArray());
}
