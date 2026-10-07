using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;
using Tripgo.Domain.Interfaces;
using Tripgo.Infrastructure.Persistence;

namespace Tripgo.Infrastructure.Services;

public class TripService : ITripService
{
    private readonly AppDbContext _context;
    private static readonly ConcurrentDictionary<long, HashSet<string>> _bookedSeatsMemory = new();
    private static readonly ConcurrentQueue<CustomerBookingInfoDto> _bookingsMemory = new();
    private static readonly ConcurrentQueue<SupportCallbackRequestDto> _supportCallbacksMemory = new();

    public TripService(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<LocationDto>> GetLocationsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Locations
            .AsNoTracking()
            .Where(l => l.IsActive)
            .OrderBy(l => l.Name)
            .Select(l => new LocationDto
            {
                Id = l.Id,
                Code = l.Code,
                Name = l.Name,
                Province = l.Province,
                Country = l.Country,
                IsActive = l.IsActive
            })
            .ToListAsync(cancellationToken);
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

        _context.Operators.Add(operatorEntity);
        await _context.SaveChangesAsync(ct);
        return operatorEntity.Id;
    }

    public async Task<long> CreateBusWithLayoutAsync(CreateBusDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (dto.OperatorId <= 0 || dto.BusTypeId <= 0 || string.IsNullOrWhiteSpace(dto.LicensePlate))
            throw new ArgumentException("Nhà xe, loại xe và biển số là bắt buộc.", nameof(dto));

        var hasOperator = await _context.Operators.AnyAsync(o => o.Id == dto.OperatorId && o.IsActive, ct);
        var hasBusType = await _context.BusTypes.AnyAsync(t => t.Id == dto.BusTypeId && t.IsActive, ct);
        if (!hasOperator || !hasBusType)
            throw new InvalidOperationException("Nhà xe hoặc loại xe không còn hoạt động.");

        var busTypeName = await _context.BusTypes
            .Where(type => type.Id == dto.BusTypeId)
            .Select(type => type.Name)
            .FirstAsync(ct);
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

        _context.Buses.Add(bus);
        await _context.SaveChangesAsync(ct);
        return bus.Id;
    }

    public async Task<List<BusTypeDto>> GetBusTypesAsync(CancellationToken ct = default)
    {
        return await _context.BusTypes
            .AsNoTracking()
            .Where(type => type.IsActive)
            .OrderBy(type => type.Name)
            .Select(type => new BusTypeDto
            {
                Id = type.Id,
                Name = type.Name,
                Description = type.Description ?? string.Empty
            })
            .ToListAsync(ct);
    }

    public async Task<List<TripSearchResultDto>> SearchTripsAsync(TripSearchRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var requestedDate = request.DepartureDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
        var start = new DateTimeOffset(requestedDate);
        var end = start.AddDays(1).AddTicks(-1);
        var windowStart = start.AddDays(-3);
        var windowEnd = end.AddDays(3);

        var matchingTrips = await _context.Trips
            .AsNoTracking()
            .Include(t => t.Route)
                .ThenInclude(r => r.Operator)
            .Include(t => t.Bus)
                .ThenInclude(b => b.BusType)
            .Include(t => t.Bus)
                .ThenInclude(b => b.SeatLayout)
            .Where(t =>
                t.Route.OriginLocationId == request.OriginLocationId &&
                t.Route.DestinationLocationId == request.DestinationLocationId &&
                t.DepartureTime >= windowStart &&
                t.DepartureTime <= windowEnd &&
                t.DepartureTime >= DateTimeOffset.Now &&
                t.Status == TripStatus.Scheduled)
            .ToListAsync(cancellationToken);

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
            var bookedCount = GetBookedSeatCount(t.Id);

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
        var trip = await _context.Trips
            .AsNoTracking()
            .Where(t => t.Id == tripId)
            .Include(t => t.Route)
                .ThenInclude(r => r.Operator)
            .Include(t => t.Bus)
                .ThenInclude(b => b.BusType)
            .Include(t => t.Bus)
                .ThenInclude(b => b.SeatLayout)
                    .ThenInclude(sl => sl.Items)
            .FirstOrDefaultAsync(cancellationToken);

        if (trip == null) return null;

        var bookedSet = GetBookedSeatSnapshot(tripId);
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
        return await _context.Operators
            .AsNoTracking()
            .Where(o => o.IsActive)
            .Select(o => new OperatorDto
            {
                Id = o.Id,
                Code = o.Code,
                Name = o.Name,
                Hotline = o.Phone,
                Address = o.Address
            })
            .ToListAsync(ct);
    }

    public async Task<List<BusSelectDto>> GetBusesByOperatorAsync(long operatorId, CancellationToken ct = default)
    {
        return await _context.Buses
            .AsNoTracking()
            .Include(b => b.BusType)
            .Include(b => b.SeatLayout)
            .Where(b => b.OperatorId == operatorId && b.IsActive)
            .Select(b => new BusSelectDto
            {
                Id = b.Id,
                OperatorId = b.OperatorId,
                BusTypeId = b.BusTypeId,
                OperatorName = b.Operator != null ? b.Operator.Name : string.Empty,
                LicensePlate = b.LicensePlate,
                Name = b.Name ?? string.Empty,
                BusTypeName = b.BusType != null ? b.BusType.Name : string.Empty,
                TotalSeats = b.SeatLayout != null ? b.SeatLayout.TotalSeats : 0
            })
            .ToListAsync(ct);
    }

    public async Task<List<RouteDto>> GetRoutesAsync(CancellationToken ct = default)
    {
        return await _context.Routes
            .AsNoTracking()
            .Include(r => r.OriginLocation)
            .Include(r => r.DestinationLocation)
            .Where(r => r.IsActive)
            .Select(r => new RouteDto
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
            })
            .ToListAsync(ct);
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

        _context.Routes.Add(route);
        await _context.SaveChangesAsync(ct);
        return route.Id;
    }

    public async Task<long> CreateTripAsync(CreateTripDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var routeOperatorId = await _context.Routes
            .Where(route => route.Id == dto.RouteId && route.IsActive)
            .Select(route => (long?)route.OperatorId)
            .FirstOrDefaultAsync(ct);
        var busOperatorId = await _context.Buses
            .Where(bus => bus.Id == dto.BusId && bus.IsActive)
            .Select(bus => (long?)bus.OperatorId)
            .FirstOrDefaultAsync(ct);
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

        _context.Trips.Add(trip);
        await _context.SaveChangesAsync(ct);
        return trip.Id;
    }

    public async Task<List<TripSearchResultDto>> GetAllTripsAdminAsync(CancellationToken ct = default)
    {
        var trips = await _context.Trips
            .AsNoTracking()
            .Include(t => t.Route)
                .ThenInclude(r => r.Operator)
            .Include(t => t.Bus)
                .ThenInclude(b => b.BusType)
            .Include(t => t.Bus)
                .ThenInclude(b => b.SeatLayout)
            .OrderByDescending(t => t.DepartureTime)
            .ToListAsync(ct);

        return trips.Select(t =>
        {
            var totalSeats = t.Bus?.SeatLayout?.TotalSeats ?? 0;
            var bookedCount = GetBookedSeatCount(t.Id);

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
        var trip = await _context.Trips.FirstOrDefaultAsync(item => item.Id == tripId, ct);
        if (trip == null)
            return false;

        trip.Status = TripStatus.Cancelled;
        trip.UpdatedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(ct);
        _bookedSeatsMemory.TryRemove(tripId, out _);
        return true;
    }

    public async Task<bool> BookSeatsAsync(long tripId, List<string> seatCodes, CustomerBookingInfoDto customerInfo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(customerInfo);
        if (seatCodes == null || seatCodes.Count == 0) return false;
        var requestedCodes = seatCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (requestedCodes.Count != seatCodes.Count) return false;

        var trip = await _context.Trips
            .AsNoTracking()
            .Include(item => item.Route).ThenInclude(route => route.Operator)
            .Include(item => item.Bus).ThenInclude(bus => bus.BusType)
            .Include(item => item.Bus).ThenInclude(bus => bus.SeatLayout).ThenInclude(layout => layout.Items)
            .FirstOrDefaultAsync(item => item.Id == tripId && item.Status == TripStatus.Scheduled, ct);
        if (trip?.Bus?.SeatLayout?.Items == null)
            return false;

        var seatCodeMap = CreateSeatCodeMap(trip.Bus.SeatLayout, trip.Bus.BusType);
        var activeSeatCodes = requestedCodes
            .Select(code => seatCodeMap.TryGetValue(code, out var internalCode) ? internalCode : null)
            .ToList();
        if (activeSeatCodes.Any(string.IsNullOrEmpty))
            return false;
        var internalCodes = activeSeatCodes.Select(code => code!).ToList();

        var currentBooked = _bookedSeatsMemory.GetOrAdd(tripId, _ => new HashSet<string>());

        lock (currentBooked)
        {
            if (internalCodes.Any(currentBooked.Contains))
            {
                return false;
            }

            foreach (var code in internalCodes)
            {
                currentBooked.Add(code);
            }

            var booking = CloneBooking(customerInfo);
            booking.TripId = trip.Id;
            booking.BookingCode = $"TRIPGO-{Convert.ToHexString(RandomNumberGenerator.GetBytes(3))[..5]}";
            booking.SeatCodes = requestedCodes;
            booking.RouteName = trip.Route?.Name ?? string.Empty;
            booking.OperatorName = trip.Route?.Operator?.Name ?? string.Empty;
            booking.BusTypeName = trip.Bus.BusType?.Name ?? string.Empty;
            booking.BusLicensePlate = trip.Bus.LicensePlate;
            booking.DepartureTime = trip.DepartureTime;
            booking.BasePrice = trip.BasePrice;
            booking.BookedAt = DateTimeOffset.UtcNow;
            booking.InsuranceAmount = booking.HasInsurance ? requestedCodes.Count * 20_000m : 0m;
            customerInfo.BookingCode = booking.BookingCode;
            _bookingsMemory.Enqueue(booking);
        }

        return true;
    }

    public Task<List<CustomerBookingInfoDto>> GetBookingsByPhoneAsync(string phone, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedPhone = NormalizePhone(phone);
        var bookings = string.IsNullOrEmpty(normalizedPhone)
            ? new List<CustomerBookingInfoDto>()
            : _bookingsMemory
                .Where(booking => NormalizePhone(booking.Phone) == normalizedPhone)
                .OrderByDescending(booking => booking.BookedAt)
                .Select(CloneBooking)
                .ToList();
        return Task.FromResult(bookings);
    }

    public Task<CustomerBookingInfoDto?> GetBookingByCodeAsync(string bookingCode, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var booking = _bookingsMemory.FirstOrDefault(item =>
            string.Equals(item.BookingCode, bookingCode?.Trim(), StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(booking == null ? null : CloneBooking(booking));
    }

    public Task<bool> SubmitSupportCallbackAsync(string name, string phone, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name) || NormalizePhone(phone).Length < 9)
            return Task.FromResult(false);

        _supportCallbacksMemory.Enqueue(new SupportCallbackRequestDto
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

    private static int GetBookedSeatCount(long tripId)
    {
        return _bookedSeatsMemory.TryGetValue(tripId, out var seats)
            ? GetSeatSnapshotCount(seats)
            : 0;
    }

    private static int GetSeatSnapshotCount(HashSet<string> seats)
    {
        lock (seats)
            return seats.Count;
    }

    private static HashSet<string> GetBookedSeatSnapshot(long tripId)
    {
        if (!_bookedSeatsMemory.TryGetValue(tripId, out var seats))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        lock (seats)
            return new HashSet<string>(seats, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizePhone(string? phone) =>
        new((phone ?? string.Empty).Where(char.IsDigit).ToArray());

    private static CustomerBookingInfoDto CloneBooking(CustomerBookingInfoDto booking) => new()
    {
        TripId = booking.TripId,
        BookingCode = booking.BookingCode,
        Name = booking.Name,
        Phone = booking.Phone,
        Email = booking.Email,
        PickupPoint = booking.PickupPoint,
        SelectedPickupPoint = booking.SelectedPickupPoint,
        SelectedDropoffPoint = booking.SelectedDropoffPoint,
        HasInsurance = booking.HasInsurance,
        SeatCodes = new List<string>(booking.SeatCodes),
        RouteName = booking.RouteName,
        OperatorName = booking.OperatorName,
        BusTypeName = booking.BusTypeName,
        BusLicensePlate = booking.BusLicensePlate,
        DepartureTime = booking.DepartureTime,
        BookedAt = booking.BookedAt,
        BasePrice = booking.BasePrice,
        InsuranceAmount = booking.InsuranceAmount
    };
}