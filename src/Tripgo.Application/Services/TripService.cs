using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tripgo.Application.DTOs;
using Tripgo.Application.Interfaces;
using Tripgo.Application.Interfaces.Persistence;
using Tripgo.Application.Services.Temporary;
using Tripgo.Domain;
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

        var code = dto.Code.Trim();
        var name = dto.Name.Trim();
        var hotline = string.IsNullOrWhiteSpace(dto.Hotline) ? null : dto.Hotline.Trim();
        var address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim();
        if (code.Length > 30 ||
            name.Length > 200 ||
            hotline?.Length > 20 ||
            address?.Length > 500)
        {
            throw new ArgumentException(
                "Mã nhà xe tối đa 30 ký tự, tên 200, hotline 20 và địa chỉ 500 ký tự.",
                nameof(dto));
        }

        var operatorEntity = new Operator
        {
            Code = code,
            Name = name,
            Phone = hotline,
            Address = address,
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

        var seatItems = ParseSeatLayout(dto.SeatLayoutDefinition);
        var hasOperator = await _repository.OperatorExistsAndActiveAsync(dto.OperatorId, ct);
        var busType = await _repository.GetActiveBusTypeByIdAsync(dto.BusTypeId, ct);
        if (!hasOperator || busType == null)
            throw new InvalidOperationException("Nhà xe hoặc loại xe không còn hoạt động.");

        var layout = new SeatLayout
        {
            Name = $"Sơ đồ xe {dto.LicensePlate.Trim()}",
            Floors = seatItems.Max(item => item.Floor),
            TotalSeats = seatItems.Count,
            IsActive = true,
            Items = seatItems
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

    public static List<SeatLayoutItem> ParseSeatLayout(string definition)
    {
        var lines = (definition ?? string.Empty)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length is 0 or > 100)
        {
            throw new ArgumentException(
                "Nhập sơ đồ gồm từ 1 đến 100 ghế, mỗi dòng theo dạng: mã ghế,tầng,hàng,cột,loại ghế.",
                nameof(definition));
        }

        var items = new List<SeatLayoutItem>(lines.Length);
        var seatCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var positions = new HashSet<(int Floor, int Row, int Column)>();

        for (var index = 0; index < lines.Length; index++)
        {
            var parts = lines[index].Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 5 ||
                string.IsNullOrWhiteSpace(parts[0]) ||
                parts[0].Length > 20 ||
                !int.TryParse(parts[1], out var floor) ||
                !int.TryParse(parts[2], out var row) ||
                !int.TryParse(parts[3], out var column) ||
                floor is < 1 or > 10 ||
                row is < 1 or > 100 ||
                column is < 1 or > 100 ||
                string.IsNullOrWhiteSpace(parts[4]) ||
                parts[4].Length > 32)
            {
                throw new ArgumentException(
                    $"Dòng sơ đồ ghế {index + 1} không hợp lệ. Dùng mã ghế,tầng,hàng,cột,loại ghế.",
                    nameof(definition));
            }

            if (!seatCodes.Add(parts[0]) || !positions.Add((floor, row, column)))
            {
                throw new ArgumentException(
                    $"Dòng sơ đồ ghế {index + 1} trùng mã ghế hoặc vị trí đã khai báo.",
                    nameof(definition));
            }

            items.Add(new SeatLayoutItem
            {
                SeatCode = parts[0],
                Floor = floor,
                Row = row,
                Column = column,
                SeatType = parts[4],
                IsActive = true
            });
        }

        return items;
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

        var vnOffset = TimeSpan.FromHours(7);
        var requestedDate = request.DepartureDate.ToDateTime(TimeOnly.MinValue);
        var start = new DateTimeOffset(requestedDate, vnOffset);
        var end = start.AddDays(1).AddTicks(-1);
        var windowStart = start;
        var windowEnd = end;

        var matchingTrips = await _repository.SearchTripsAsync(
            request.OriginLocationId,
            request.DestinationLocationId,
            windowStart,
            windowEnd,
            cancellationToken);

        var targetDate = requestedDate.Date;
        // A sandbox departure whose sales have closed is no longer on offer, so it leaves the results.
        var now = DateTimeOffset.UtcNow;
        var exactDateTrips = matchingTrips
            .Where(trip => trip.DepartureTime.ToOffset(vnOffset).Date == targetDate)
            .Where(trip => !IsSandboxDemoTrip(trip) || BookingRules.GetSaleBlockReason(trip, now) == null)
            .ToList();

        var trips = exactDateTrips;

        return trips.Select(t =>
        {
            var isDemo = IsSandboxDemoTrip(t);
            var saleBlockReason = BookingRules.GetSaleBlockReason(t, DateTimeOffset.UtcNow);
            var isBookable = isDemo && saleBlockReason == null;
            var totalSeats = t.Bus?.SeatLayout?.TotalSeats ?? 0;
            var (pickups, dropoffs) = ExtractTripStops(t);
            var operatorHotline = t.Route?.Operator?.Phone ?? string.Empty;

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
                AvailableSeats = CountSellableSeats(t, isDemo),
                PickupPoints = pickups,
                DropoffPoints = dropoffs,
                IsBookable = isBookable,
                OperationalTier = isDemo ? "DEMO_SANDBOX_TRIP" : "PUBLISHED_REFERENCE_SCHEDULE",
                OperationalTierLabel = isDemo ? "Chuyến xe chạy thử nghiệm (Demo Sandbox)" : "Lịch trình tham khảo",
                OperatorHotline = operatorHotline
            };
        }).ToList();
    }

    private static bool IsSandboxDemoTrip(Trip t) => BookingRules.IsSandboxTrip(t);

    /// <summary>
    /// Counts what a customer could actually select. A trip whose inventory has not been created yet
    /// gets it from the configured layout on first use, so the layout total is the honest figure.
    /// </summary>
    private static int CountSellableSeats(Trip trip, bool isSandbox)
    {
        if (!isSandbox)
        {
            return 0;
        }

        var total = trip.Bus?.SeatLayout?.TotalSeats ?? trip.Seats?.Count ?? 0;
        var unavailable = trip.Seats?.Count(seat => seat.Status != TripSeatStatus.Available) ?? 0;
        return Math.Max(0, total - unavailable);
    }

    public async Task<TripDetailDto?> GetTripDetailsAsync(long tripId, CancellationToken cancellationToken = default)
    {
        var trip = await _repository.GetTripWithDetailsAsync(tripId, cancellationToken);
        if (trip == null) return null;

        var isDemo = IsSandboxDemoTrip(trip);
        var (pickupPoints, dropoffPoints) = ExtractTripStops(trip);
        var operatorHotline = trip.Route?.Operator?.Phone ?? string.Empty;

        List<SeatLayoutItemDto> seatItems;
        int availableSeats = 0;

        if (isDemo && _bookingService != null)
        {
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
                Status = s.Status,
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
            IsBookable = isDemo && BookingRules.GetSaleBlockReason(trip, DateTimeOffset.UtcNow) == null,
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
            Address = o.Address,
            IsVerified = o.IsVerified
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
        ArgumentNullException.ThrowIfNull(dto);
        if (dto.OperatorId <= 0 ||
            dto.OriginLocationId <= 0 ||
            dto.DestinationLocationId <= 0 ||
            dto.OriginLocationId == dto.DestinationLocationId ||
            string.IsNullOrWhiteSpace(dto.Name) ||
            dto.DistanceKm <= 0 ||
            dto.EstimatedDurationMinutes <= 0)
        {
            throw new ArgumentException(
                "Nhà xe, hai điểm khác nhau, tên tuyến, cự ly và thời lượng hợp lệ là bắt buộc.",
                nameof(dto));
        }

        if (dto.Name.Trim().Length > 200 ||
            (!string.IsNullOrWhiteSpace(dto.Code) && dto.Code.Trim().Length > 50))
        {
            throw new ArgumentException(
                "Tên tuyến tối đa 200 ký tự và mã tuyến tối đa 50 ký tự.",
                nameof(dto));
        }

        if (!await _repository.OperatorExistsAndActiveAsync(dto.OperatorId, ct))
            throw new InvalidOperationException("Nhà xe không còn hoạt động.");

        var activeLocationIds = (await _repository.GetActiveLocationsAsync(ct))
            .Select(location => location.Id)
            .ToHashSet();
        if (!activeLocationIds.Contains(dto.OriginLocationId) ||
            !activeLocationIds.Contains(dto.DestinationLocationId))
        {
            throw new InvalidOperationException("Điểm đi hoặc điểm đến không còn hoạt động.");
        }

        var route = new Route
        {
            OperatorId = dto.OperatorId,
            OriginLocationId = dto.OriginLocationId,
            DestinationLocationId = dto.DestinationLocationId,
            Code = string.IsNullOrWhiteSpace(dto.Code) ? $"R-{Guid.NewGuid():N}" : dto.Code.Trim(),
            Name = dto.Name.Trim(),
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
        if (dto.RouteId <= 0 || dto.BusId <= 0)
            throw new ArgumentException("Tuyến đường và xe là bắt buộc.", nameof(dto));

        if (dto.DepartureTime <= DateTimeOffset.UtcNow ||
            dto.ExpectedArrivalTime <= dto.DepartureTime)
        {
            throw new ArgumentException(
                "Giờ đến dự kiến phải sau giờ khởi hành trong tương lai.",
                nameof(dto));
        }

        if (dto.BasePrice <= 0)
            throw new ArgumentException("Giá tham khảo phải lớn hơn 0.", nameof(dto));

        var routeOperatorId = await _repository.GetRouteOperatorIdAsync(dto.RouteId, ct);
        var busOperatorId = await _repository.GetBusOperatorIdAsync(dto.BusId, ct);
        if (routeOperatorId == null || busOperatorId == null || routeOperatorId != busOperatorId)
            throw new InvalidOperationException("Tuyến và xe phải thuộc cùng một nhà xe đang hoạt động.");

        if (await _repository.HasConflictingScheduledTripAsync(
                dto.BusId,
                dto.DepartureTime,
                dto.ExpectedArrivalTime,
                ct))
        {
            throw new InvalidOperationException(
                "Xe đã có lịch trình trùng thời gian. Chọn xe hoặc thời gian khác.");
        }

        var trip = new Trip
        {
            RouteId = dto.RouteId,
            BusId = dto.BusId,
            TripCode = $"TRIP-{Guid.NewGuid():N}",
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
            var isDemo = IsSandboxDemoTrip(t);
            var totalSeats = t.Bus?.SeatLayout?.TotalSeats ?? (t.Seats?.Count ?? 0);

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
                AvailableSeats = CountSellableSeats(t, isDemo),
                IsBookable = isDemo && BookingRules.GetSaleBlockReason(t, DateTimeOffset.UtcNow) == null,
                OperationalTier = isDemo ? "DEMO_SANDBOX_TRIP" : "PUBLISHED_REFERENCE_SCHEDULE",
                OperationalTierLabel = isDemo
                    ? "Chuyến xe chạy thử nghiệm (Demo Sandbox)"
                    : "Lịch trình tham khảo"
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

        var isDemo = IsSandboxDemoTrip(trip);
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
            ContactEmailConfirmed = customerInfo.ContactEmailConfirmed,
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
                    ExpiresAt = persistent.ExpiresAt,
                    BasePrice = persistent.BaseAmount,
                    SurchargeAmount = persistent.SurchargeAmount,
                    InsuranceAmount = persistent.InsuranceAmount,
                    TotalAmount = persistent.TotalAmount,
                    BookingStatus = persistent.Status,
                    PaymentStatus = persistent.PaymentStatus,
                    IsDemoBooking = persistent.IsDemoBooking,
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
                    ExpiresAt = persistent.ExpiresAt,
                    BasePrice = persistent.BaseAmount,
                    SurchargeAmount = persistent.SurchargeAmount,
                    InsuranceAmount = persistent.InsuranceAmount,
                    TotalAmount = persistent.TotalAmount,
                    BookingStatus = persistent.Status,
                    PaymentStatus = persistent.PaymentStatus,
                    IsDemoBooking = persistent.IsDemoBooking,
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
                ExpiresAt = persistent.ExpiresAt,
                BasePrice = persistent.BaseAmount,
                SurchargeAmount = persistent.SurchargeAmount,
                InsuranceAmount = persistent.InsuranceAmount,
                TotalAmount = persistent.TotalAmount,
                BookingStatus = persistent.Status,
                PaymentStatus = persistent.PaymentStatus,
                IsDemoBooking = persistent.IsDemoBooking,
                ConfirmedAt = persistent.ConfirmedAt,
                TransactionCode = persistent.TransactionCode,
                PaymentMethod = persistent.PaymentMethod,
                Passengers = persistent.Passengers,
                PaymentTransactions = persistent.PaymentTransactions
            }).ToList();
        }

        return new List<CustomerBookingInfoDto>();
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
                        ? stop.ScheduledDepartureTime!.Value.ToVietnam().ToString("HH:mm")
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
                        ? stop.ScheduledArrivalTime!.Value.ToVietnam().ToString("HH:mm")
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
                        ? trip.DepartureTime.AddMinutes(stop.DepartureOffsetMinutes!.Value).ToVietnam().ToString("HH:mm")
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
                        ? trip.DepartureTime.AddMinutes(stop.ArrivalOffsetMinutes!.Value).ToVietnam().ToString("HH:mm")
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
