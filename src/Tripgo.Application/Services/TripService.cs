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
    private readonly IBookingService? _bookingService;

    public TripService(ITripRepository repository, ITemporaryBookingStore bookingStore, IBookingService bookingService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _bookingStore = bookingStore ?? throw new ArgumentNullException(nameof(bookingStore));
        _bookingService = bookingService;
    }

    public TripService(ITripRepository repository, ITemporaryBookingStore bookingStore)
        : this(repository, bookingStore, null!)
    {
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
            var isDemo = t.IsDemoTrip || t.TripCode.StartsWith("DEMO-", StringComparison.OrdinalIgnoreCase);
            var isBookable = isDemo;
            var totalSeats = t.Bus?.SeatLayout?.TotalSeats ?? 0;
            var bookedCount = isDemo ? (t.Seats?.Count(s => s.Status == TripSeatStatus.Booked) ?? 0) : 0;
            var (pickups, dropoffs) = ExtractTripStops(t);
            var operatorHotline = t.Route?.Operator?.Phone ?? "1900 8888";

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
                AvailableSeats = isDemo ? Math.Max(0, totalSeats - bookedCount) : 0,
                PickupPoints = pickups,
                DropoffPoints = dropoffs,
                IsBookable = isBookable,
                OperationalTier = isDemo ? "DEMO_SANDBOX_TRIP" : "PUBLISHED_REFERENCE_SCHEDULE",
                OperationalTierLabel = isDemo ? "Chuyến xe chạy thử nghiệm (Demo Sandbox)" : "Lịch trình tham khảo",
                OperatorHotline = operatorHotline
            };
        }).ToList();
    }

    public async Task<TripDetailDto?> GetTripDetailsAsync(long tripId, CancellationToken cancellationToken = default)
    {
        var trip = await _repository.GetTripWithDetailsAsync(tripId, cancellationToken);
        if (trip == null) return null;

        var isDemo = trip.IsDemoTrip || trip.TripCode.StartsWith("DEMO-", StringComparison.OrdinalIgnoreCase);
        var (pickupPoints, dropoffPoints) = ExtractTripStops(trip);
        var operatorHotline = trip.Route?.Operator?.Phone ?? "1900 8888";

        List<SeatLayoutItemDto> seatItems;
        int availableSeats = 0;

        if (isDemo && _bookingService != null)
        {
            await _bookingService.EnsureDemoTripInventoryAsync(tripId, cancellationToken);
            var tripSeats = await _bookingService.GetSeatsForTripAsync(tripId, cancellationToken);
            seatItems = tripSeats.Select(s => new SeatLayoutItemDto
            {
                Id = s.Id,
                SeatCode = s.SeatCode,
                Floor = s.Floor,
                Row = s.Row,
                Column = s.Column,
                SeatType = s.SeatType,
                IsAvailable = s.IsAvailable,
                Price = s.Price
            }).ToList();
            availableSeats = seatItems.Count(s => s.IsAvailable);
        }
        else
        {
            seatItems = new List<SeatLayoutItemDto>();
            availableSeats = 0;
        }

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
            TotalSeats = trip.Bus?.SeatLayout?.TotalSeats ?? seatItems.Count,
            AvailableSeats = availableSeats,
            Seats = seatItems,
            PickupPoints = pickupPoints,
            DropoffPoints = dropoffPoints,
            IsBookable = isDemo,
            OperationalTier = isDemo ? "DEMO_SANDBOX_TRIP" : "PUBLISHED_REFERENCE_SCHEDULE",
            OperationalTierLabel = isDemo ? "Chuyến xe chạy thử nghiệm (Demo Sandbox)" : "Lịch trình tham khảo",
            OperatorHotline = operatorHotline
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
            var totalSeats = t.Bus?.SeatLayout?.TotalSeats ?? (t.Seats?.Count ?? 0);
            var bookedCount = t.Seats?.Count(s => s.Status == TripSeatStatus.Booked) ?? 0;

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
        return await _repository.CancelTripAsync(tripId, ct);
    }

    public async Task<bool> BookSeatsAsync(long tripId, List<string> seatCodes, CustomerBookingInfoDto customerInfo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(customerInfo);
        if (seatCodes == null || seatCodes.Count == 0) return false;

        var trip = await _repository.GetTripWithDetailsAsync(tripId, ct);
        if (trip == null) return false;

        var isDemo = trip.IsDemoTrip || trip.TripCode.StartsWith("DEMO-", StringComparison.OrdinalIgnoreCase);
        if (!isDemo || _bookingService == null)
        {
            // Reference trips strictly reject booking
            return false;
        }

        var holdResult = await _bookingService.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = tripId,
            SeatCodes = seatCodes,
            OwnerIdentifier = customerInfo.OwnerIdentifier,
            HoldMinutes = 10
        }, ct);

        if (!holdResult.Success || string.IsNullOrEmpty(holdResult.HoldToken))
        {
            return false;
        }

        var bookingResult = await _bookingService.CreateBookingAsync(new CreateBookingRequestDto
        {
            TripId = tripId,
            HoldToken = holdResult.HoldToken,
            OwnerIdentifier = customerInfo.OwnerIdentifier,
            SeatCodes = seatCodes,
            PickupPointId = customerInfo.PickupPointId,
            DropoffPointId = customerInfo.DropoffPointId,
            PassengerName = customerInfo.Name,
            PassengerPhone = customerInfo.Phone,
            PassengerEmail = customerInfo.Email,
            HasInsurance = customerInfo.HasInsurance,
            Passengers = customerInfo.Passengers
        }, ct);

        if (bookingResult.Success && !string.IsNullOrEmpty(bookingResult.BookingCode))
        {
            customerInfo.BookingCode = bookingResult.BookingCode;
            customerInfo.TotalAmount = bookingResult.TotalAmount;
            customerInfo.BookingStatus = BookingStatus.PendingPayment.ToString();
            customerInfo.PaymentStatus = PaymentStatus.Unpaid.ToString();
            return true;
        }

        return false;
    }

    public async Task<List<CustomerBookingInfoDto>> GetBookingsByPhoneAsync(string phone, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_bookingService != null)
        {
            var persistentList = await _bookingService.GetBookingsByPhoneAsync(phone, ct);
            return persistentList.Select(persistent => new CustomerBookingInfoDto
            {
                    BookingId = persistent.Id,
                    TripId = persistent.TripId,
                    BookingCode = persistent.BookingCode,
                    Name = persistent.PassengerName,
                    Phone = persistent.PassengerPhone,
                    Email = persistent.PassengerEmail,
                    SelectedPickupPoint = persistent.PickupPointName,
                    SelectedDropoffPoint = persistent.DropoffPointName,
                    PickupPoint = persistent.PickupPointName,
                    HasInsurance = persistent.InsuranceAmount > 0,
                    SeatCodes = persistent.SeatCodes,
                    RouteName = persistent.RouteName,
                    OperatorName = persistent.OperatorName,
                    BusTypeName = persistent.BusTypeName,
                    BusLicensePlate = persistent.BusLicensePlate,
                    DepartureTime = persistent.DepartureTime,
                    BookedAt = persistent.CreatedAt,
                    BasePrice = persistent.BaseAmount,
                    SurchargeAmount = persistent.SurchargeAmount,
                    InsuranceAmount = persistent.InsuranceAmount,
                    TotalAmount = persistent.TotalAmount,
                    BookingStatus = persistent.Status,
                    PaymentStatus = persistent.PaymentStatus,
                    ConfirmedAt = persistent.ConfirmedAt,
                    TransactionCode = persistent.TransactionCode,
                    PaymentMethod = persistent.PaymentMethod,
                    Passengers = persistent.Passengers,
                    PaymentTransactions = persistent.PaymentTransactions
            }).ToList();
        }

        return new List<CustomerBookingInfoDto>();
    }

    public async Task<CustomerBookingInfoDto?> GetBookingByCodeAsync(string bookingCode, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_bookingService != null)
        {
            var persistent = await _bookingService.GetBookingByCodeAsync(bookingCode, ct);
            if (persistent != null)
            {
                return new CustomerBookingInfoDto
                {
                    BookingId = persistent.Id,
                    TripId = persistent.TripId,
                    BookingCode = persistent.BookingCode,
                    Name = persistent.PassengerName,
                    Phone = persistent.PassengerPhone,
                    Email = persistent.PassengerEmail,
                    SelectedPickupPoint = persistent.PickupPointName,
                    SelectedDropoffPoint = persistent.DropoffPointName,
                    PickupPoint = persistent.PickupPointName,
                    HasInsurance = persistent.InsuranceAmount > 0,
                    SeatCodes = persistent.SeatCodes,
                    RouteName = persistent.RouteName,
                    OperatorName = persistent.OperatorName,
                    BusTypeName = persistent.BusTypeName,
                    BusLicensePlate = persistent.BusLicensePlate,
                    DepartureTime = persistent.DepartureTime,
                    BookedAt = persistent.CreatedAt,
                    BasePrice = persistent.BaseAmount,
                    SurchargeAmount = persistent.SurchargeAmount,
                    InsuranceAmount = persistent.InsuranceAmount,
                    TotalAmount = persistent.TotalAmount,
                    BookingStatus = persistent.Status,
                    PaymentStatus = persistent.PaymentStatus,
                    ConfirmedAt = persistent.ConfirmedAt,
                    TransactionCode = persistent.TransactionCode,
                    PaymentMethod = persistent.PaymentMethod,
                    Passengers = persistent.Passengers,
                    PaymentTransactions = persistent.PaymentTransactions
                };
            }
        }

        return null;
    }

    public async Task<List<CustomerBookingInfoDto>> GetBookingsForCurrentUserAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_bookingService != null)
        {
            var persistentList = await _bookingService.GetBookingsForCurrentUserAsync(ct);
            return persistentList.Select(persistent => new CustomerBookingInfoDto
            {
                BookingId = persistent.Id,
                TripId = persistent.TripId,
                BookingCode = persistent.BookingCode,
                Name = persistent.PassengerName,
                Phone = persistent.PassengerPhone,
                Email = persistent.PassengerEmail,
                SelectedPickupPoint = persistent.PickupPointName,
                SelectedDropoffPoint = persistent.DropoffPointName,
                PickupPoint = persistent.PickupPointName,
                HasInsurance = persistent.InsuranceAmount > 0,
                SeatCodes = persistent.SeatCodes,
                RouteName = persistent.RouteName,
                OperatorName = persistent.OperatorName,
                BusTypeName = persistent.BusTypeName,
                BusLicensePlate = persistent.BusLicensePlate,
                DepartureTime = persistent.DepartureTime,
                BookedAt = persistent.CreatedAt,
                BasePrice = persistent.BaseAmount,
                SurchargeAmount = persistent.SurchargeAmount,
                InsuranceAmount = persistent.InsuranceAmount,
                TotalAmount = persistent.TotalAmount,
                BookingStatus = persistent.Status,
                PaymentStatus = persistent.PaymentStatus,
                ConfirmedAt = persistent.ConfirmedAt,
                TransactionCode = persistent.TransactionCode,
                PaymentMethod = persistent.PaymentMethod,
                Passengers = persistent.Passengers,
                PaymentTransactions = persistent.PaymentTransactions
            }).ToList();
        }

        return new List<CustomerBookingInfoDto>();
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

    internal static (List<PickupDropoffPointDto> PickupPoints, List<PickupDropoffPointDto> DropoffPoints) ExtractTripStops(Trip trip)
    {
        var pickupPoints = new List<PickupDropoffPointDto>();
        var dropoffPoints = new List<PickupDropoffPointDto>();

        // 1. Prioritize scheduled TripStops if configured
        if (trip.Stops != null && trip.Stops.Count > 0)
        {
            var stops = trip.Stops.OrderBy(s => s.StopOrder).ToList();
            foreach (var stop in stops)
            {
                if (stop.Point == null) continue;
                if (stop.AllowPickup)
                {
                    var hasFixed = stop.ScheduledDepartureTime.HasValue;
                    var time = hasFixed
                        ? stop.ScheduledDepartureTime!.Value.ToLocalTime().ToString("HH:mm")
                        : PickupDropoffPointDto.UnknownTimeText;
                    pickupPoints.Add(new PickupDropoffPointDto
                    {
                        Id = stop.Point.Id,
                        Name = stop.Point.Name,
                        Address = stop.Point.Address,
                        Time = time,
                        HasFixedTime = hasFixed,
                        IsPickup = true
                    });
                }
                if (stop.AllowDropoff)
                {
                    var hasFixed = stop.ScheduledArrivalTime.HasValue;
                    var time = hasFixed
                        ? stop.ScheduledArrivalTime!.Value.ToLocalTime().ToString("HH:mm")
                        : PickupDropoffPointDto.UnknownTimeText;
                    dropoffPoints.Add(new PickupDropoffPointDto
                    {
                        Id = stop.Point.Id,
                        Name = stop.Point.Name,
                        Address = stop.Point.Address,
                        Time = time,
                        HasFixedTime = hasFixed,
                        IsPickup = false
                    });
                }
            }
        }
        // 2. Otherwise check template RouteStops
        else if (trip.Route?.Stops != null && trip.Route.Stops.Count > 0)
        {
            var rStops = trip.Route.Stops.OrderBy(s => s.StopOrder).ToList();
            foreach (var stop in rStops)
            {
                if (stop.Point == null) continue;
                if (stop.AllowPickup)
                {
                    var hasFixed = stop.DepartureOffsetMinutes.HasValue;
                    var depTime = hasFixed
                        ? trip.DepartureTime.AddMinutes(stop.DepartureOffsetMinutes!.Value).ToLocalTime().ToString("HH:mm")
                        : PickupDropoffPointDto.UnknownTimeText;
                    pickupPoints.Add(new PickupDropoffPointDto
                    {
                        Id = stop.Point.Id,
                        Name = stop.Point.Name,
                        Address = stop.Point.Address,
                        Time = depTime,
                        HasFixedTime = hasFixed,
                        IsPickup = true
                    });
                }
                if (stop.AllowDropoff)
                {
                    var hasFixed = stop.ArrivalOffsetMinutes.HasValue;
                    var arrTime = hasFixed
                        ? trip.DepartureTime.AddMinutes(stop.ArrivalOffsetMinutes!.Value).ToLocalTime().ToString("HH:mm")
                        : PickupDropoffPointDto.UnknownTimeText;
                    dropoffPoints.Add(new PickupDropoffPointDto
                    {
                        Id = stop.Point.Id,
                        Name = stop.Point.Name,
                        Address = stop.Point.Address,
                        Time = arrTime,
                        HasFixedTime = hasFixed,
                        IsPickup = false
                    });
                }
            }
        }

        // Return verified points only. Do NOT fabricate dummy fallback stops when DB has no stops.
        return (pickupPoints, dropoffPoints);
    }

    private static string NormalizePhone(string? phone) =>
        new((phone ?? string.Empty).Where(char.IsDigit).ToArray());
}
