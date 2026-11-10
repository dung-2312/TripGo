using System.Data;
using Microsoft.EntityFrameworkCore;
using Tripgo.Application.DTOs.Admin;
using Tripgo.Application.Interfaces;
using Tripgo.Application.Interfaces.Persistence;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;

namespace Tripgo.Infrastructure.Persistence.Repositories;

public sealed class AdminRepository : IAdminRepository
{
    private readonly AppDbContext _context;

    public AdminRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<AdminActor?> GetActiveActorAsync(long userId, CancellationToken ct = default)
    {
        return await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive && u.Role != null)
            .Select(u => new AdminActor(u.Id, u.Email, u.Role!.Name, u.OperatorId))
            .FirstOrDefaultAsync(ct);
    }

    public async Task AddAuditAsync(AdminAuditLog entry, CancellationToken ct = default)
    {
        _context.AdminAuditLogs.Add(entry);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<AdminDashboardDto> GetDashboardAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var bookingCounts = await _context.Bookings.AsNoTracking()
            .GroupBy(b => b.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        int Bookings(BookingStatus status) => bookingCounts.FirstOrDefault(x => x.Status == status)?.Count ?? 0;

        var paid = await _context.PaymentTransactions.AsNoTracking()
            .Where(t => t.Status == PaymentStatus.Paid)
            .GroupBy(t => t.IsSandbox)
            .Select(g => new { IsSandbox = g.Key, Count = g.Count(), Total = g.Sum(t => t.Amount) })
            .ToListAsync(ct);
        var sandboxPaid = paid.FirstOrDefault(x => x.IsSandbox);
        var realPaid = paid.FirstOrDefault(x => !x.IsSandbox);

        var reviewOutcomes = NeedsReviewOutcomes;

        var topRoutesRaw = await _context.Bookings.AsNoTracking()
            .Where(b => b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.PendingPayment)
            .GroupBy(b => new { RouteName = b.Trip.Route.Name, OperatorName = b.Trip.Route.Operator.Name })
            .Select(g => new
            {
                g.Key.RouteName,
                g.Key.OperatorName,
                BookingCount = g.Count(),
                TotalRevenue = g.Sum(b => b.TotalAmount)
            })
            .OrderByDescending(r => r.BookingCount)
            .Take(5)
            .ToListAsync(ct);

        var topRoutes = topRoutesRaw.Select(r => new AdminDashboardTopRouteDto
        {
            RouteName = r.RouteName,
            OperatorName = r.OperatorName,
            BookingCount = r.BookingCount,
            TotalRevenue = r.TotalRevenue
        }).ToList();

        var recentRows = await _context.Bookings.AsNoTracking()
            .OrderByDescending(b => b.CreatedAt)
            .Take(5)
            .Select(b => new
            {
                b.Id,
                b.BookingCode,
                RouteName = b.Trip.Route.Name,
                b.Trip.DepartureTime,
                b.PassengerName,
                b.PassengerPhone,
                b.Status,
                b.PaymentStatus,
                b.TotalAmount,
                b.CreatedAt,
                b.IsDemoBooking
            })
            .ToListAsync(ct);

        var recentBookings = recentRows.Select(b => new AdminBookingRowDto
        {
            Id = b.Id,
            BookingCode = b.BookingCode,
            RouteName = b.RouteName,
            DepartureTime = b.DepartureTime,
            MaskedPassengerName = PrivacyMask.Name(b.PassengerName),
            MaskedPhone = PrivacyMask.Phone(b.PassengerPhone),
            Status = b.Status.ToString(),
            PaymentStatus = b.PaymentStatus.ToString(),
            TotalAmount = b.TotalAmount,
            CreatedAt = b.CreatedAt,
            IsSandbox = b.IsDemoBooking
        }).ToList();

        return new AdminDashboardDto
        {
            TotalBookings = bookingCounts.Sum(x => x.Count),
            PendingBookings = Bookings(BookingStatus.PendingPayment),
            ConfirmedBookings = Bookings(BookingStatus.Confirmed),
            CancelledBookings = Bookings(BookingStatus.Cancelled),
            ExpiredBookings = Bookings(BookingStatus.Expired),
            UpcomingTrips = await _context.Trips.CountAsync(t => t.Status == TripStatus.Scheduled && t.DepartureTime > now, ct),
            SandboxTrips = await _context.Trips.CountAsync(t => t.IsDemoTrip && t.Status == TripStatus.Scheduled && t.DepartureTime > now, ct),
            ReferenceTrips = await _context.Trips.CountAsync(t => !t.IsDemoTrip && t.Status == TripStatus.Scheduled && t.DepartureTime > now, ct),
            CancelledTrips = await _context.Trips.CountAsync(t => t.Status == TripStatus.Cancelled, ct),
            ActiveOperators = await _context.Operators.CountAsync(o => o.IsActive, ct),
            InactiveOperators = await _context.Operators.CountAsync(o => !o.IsActive, ct),
            VerifiedOperators = await _context.Operators.CountAsync(o => o.IsVerified, ct),
            ActiveRoutes = await _context.Routes.CountAsync(r => r.IsActive, ct),
            ActiveBuses = await _context.Buses.CountAsync(b => b.IsActive, ct),
            SandboxPaidAmount = sandboxPaid?.Total,
            SandboxPaidTransactions = sandboxPaid?.Count ?? 0,
            RealPaidAmount = realPaid?.Total,
            RealPaidTransactions = realPaid?.Count ?? 0,
            EmailsPending = await _context.BookingConfirmationEmailOutbox.CountAsync(e => e.Status == BookingConfirmationEmailOutbox.Pending, ct),
            EmailsFailed = await _context.BookingConfirmationEmailOutbox.CountAsync(e => e.Status == BookingConfirmationEmailOutbox.Failed, ct),
            PaymentEventsNeedingReview = await _context.PaymentWebhookEvents.CountAsync(e => reviewOutcomes.Contains(e.Outcome), ct),
            TopRoutes = topRoutes,
            RecentBookings = recentBookings
        };
    }

    private static readonly string[] NeedsReviewOutcomes =
    {
        PaymentWebhookEvent.OutcomeAmountMismatch,
        PaymentWebhookEvent.OutcomeCurrencyMismatch,
        PaymentWebhookEvent.OutcomeLatePayment,
        PaymentWebhookEvent.OutcomeUnknownTransaction,
        PaymentWebhookEvent.OutcomeInvalidSignature
    };

    // ---------------------------------------------------------------- Operators

    public async Task<List<AdminOperatorDto>> GetOperatorsAsync(string? search, CancellationToken ct = default)
    {
        var query = _context.Operators.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(o => o.Name.Contains(term) || o.Code.Contains(term));
        }

        return await query
            .OrderBy(o => o.Name)
            .Select(o => new AdminOperatorDto
            {
                Id = o.Id,
                Code = o.Code,
                Name = o.Name,
                LegalName = o.LegalName,
                TaxCode = o.TaxCode,
                Phone = o.Phone,
                Email = o.Email,
                Address = o.Address,
                Description = o.Description,
                IsActive = o.IsActive,
                IsVerified = o.IsVerified,
                VerificationEvidence = o.VerificationEvidence,
                VerifiedAt = o.VerifiedAt,
                DataSource = o.DataSource.ToString(),
                SourceReference = o.SourceReference,
                RouteCount = _context.Routes.Count(r => r.OperatorId == o.Id),
                BusCount = _context.Buses.Count(b => b.OperatorId == o.Id)
            })
            .ToListAsync(ct);
    }

    public Task<bool> OperatorCodeOrTaxCodeTakenAsync(string code, string? taxCode, long exceptId, CancellationToken ct = default) =>
        _context.Operators.AnyAsync(o => o.Id != exceptId && (o.Code == code || (taxCode != null && o.TaxCode == taxCode)), ct);

    public async Task<bool> UpdateOperatorAsync(AdminOperatorInput input, CancellationToken ct = default)
    {
        _context.ChangeTracker.Clear();
        var entity = await _context.Operators.FirstOrDefaultAsync(o => o.Id == input.Id, ct);
        if (entity == null) return false;

        entity.Code = input.Code;
        entity.Name = input.Name;
        entity.LegalName = input.LegalName;
        entity.TaxCode = input.TaxCode;
        entity.Phone = input.Phone;
        entity.Email = input.Email;
        entity.Address = input.Address;
        entity.Description = input.Description;
        entity.SourceReference = input.SourceReference;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetOperatorActiveAsync(long operatorId, bool isActive, CancellationToken ct = default)
    {
        _context.ChangeTracker.Clear();
        var entity = await _context.Operators.FirstOrDefaultAsync(o => o.Id == operatorId, ct);
        if (entity == null) return false;

        entity.IsActive = isActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetOperatorVerificationAsync(long operatorId, bool isVerified, string? evidence, long actorUserId, DateTimeOffset now, CancellationToken ct = default)
    {
        _context.ChangeTracker.Clear();
        var entity = await _context.Operators.FirstOrDefaultAsync(o => o.Id == operatorId, ct);
        if (entity == null) return false;

        entity.IsVerified = isVerified;
        entity.VerificationEvidence = isVerified ? evidence : null;
        entity.VerifiedAt = isVerified ? now : null;
        entity.VerifiedByUserId = isVerified ? actorUserId : null;
        entity.DataSource = isVerified
            ? TransportDataSource.OperatorVerified
            : entity.DataSource == TransportDataSource.OperatorVerified ? TransportDataSource.Unverified : entity.DataSource;
        entity.UpdatedAt = now;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    // ---------------------------------------------------------------- Routes

    public async Task<List<AdminRouteDto>> GetRoutesAsync(bool includeInactive, CancellationToken ct = default)
    {
        return await _context.Routes.AsNoTracking()
            .Where(r => includeInactive || r.IsActive)
            .OrderByDescending(r => r.IsActive).ThenBy(r => r.Name)
            .Select(r => new AdminRouteDto
            {
                Id = r.Id,
                OperatorId = r.OperatorId,
                OperatorName = r.Operator.Name,
                Code = r.Code,
                Name = r.Name,
                OriginLocationId = r.OriginLocationId,
                DestinationLocationId = r.DestinationLocationId,
                OriginName = r.OriginLocation.Name,
                DestinationName = r.DestinationLocation.Name,
                DistanceKm = r.DistanceKm,
                EstimatedDurationMinutes = r.EstimatedDurationMinutes,
                IsActive = r.IsActive,
                DataSource = r.DataSource.ToString(),
                SourceReference = r.SourceReference,
                StopCount = r.Stops.Count
            })
            .ToListAsync(ct);
    }

    public async Task<AdminRouteDto?> GetRouteAsync(long routeId, CancellationToken ct = default)
    {
        var route = (await _context.Routes.AsNoTracking()
            .Where(r => r.Id == routeId)
            .Select(r => new AdminRouteDto
            {
                Id = r.Id,
                OperatorId = r.OperatorId,
                OperatorName = r.Operator.Name,
                Code = r.Code,
                Name = r.Name,
                OriginLocationId = r.OriginLocationId,
                DestinationLocationId = r.DestinationLocationId,
                OriginName = r.OriginLocation.Name,
                DestinationName = r.DestinationLocation.Name,
                DistanceKm = r.DistanceKm,
                EstimatedDurationMinutes = r.EstimatedDurationMinutes,
                IsActive = r.IsActive,
                DataSource = r.DataSource.ToString(),
                SourceReference = r.SourceReference,
                StopCount = r.Stops.Count
            })
            .FirstOrDefaultAsync(ct));
        if (route == null) return null;

        route.Stops.AddRange(await _context.RouteStops.AsNoTracking()
            .Where(s => s.RouteId == routeId)
            .OrderBy(s => s.StopOrder)
            .Select(s => new AdminRouteStopDto
            {
                PointId = s.PointId,
                PointName = s.Point.Name,
                StopOrder = s.StopOrder,
                AllowPickup = s.AllowPickup,
                AllowDropoff = s.AllowDropoff,
                ArrivalOffsetMinutes = s.ArrivalOffsetMinutes,
                DepartureOffsetMinutes = s.DepartureOffsetMinutes
            })
            .ToListAsync(ct));
        return route;
    }

    public Task<bool> ActiveRouteExistsAsync(long operatorId, long originId, long destinationId, long exceptId, CancellationToken ct = default) =>
        _context.Routes.AnyAsync(r =>
            r.Id != exceptId && r.IsActive && r.OperatorId == operatorId &&
            r.OriginLocationId == originId && r.DestinationLocationId == destinationId, ct);

    public Task<bool> RouteCodeTakenAsync(string code, long exceptId, CancellationToken ct = default) =>
        _context.Routes.AnyAsync(r => r.Id != exceptId && r.Code == code, ct);

    public async Task<bool> UpdateRouteAsync(AdminRouteInput input, CancellationToken ct = default)
    {
        _context.ChangeTracker.Clear();
        var entity = await _context.Routes.FirstOrDefaultAsync(r => r.Id == input.Id, ct);
        if (entity == null) return false;

        entity.OperatorId = input.OperatorId;
        entity.OriginLocationId = input.OriginLocationId;
        entity.DestinationLocationId = input.DestinationLocationId;
        if (!string.IsNullOrWhiteSpace(input.Code)) entity.Code = input.Code;
        entity.Name = input.Name;
        entity.DistanceKm = input.DistanceKm;
        entity.EstimatedDurationMinutes = input.EstimatedDurationMinutes;
        entity.SourceReference = string.IsNullOrWhiteSpace(input.SourceReference) ? null : input.SourceReference.Trim();
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetRouteSourceReferenceAsync(long routeId, string? sourceReference, CancellationToken ct = default)
    {
        _context.ChangeTracker.Clear();
        var entity = await _context.Routes.FirstOrDefaultAsync(r => r.Id == routeId, ct);
        if (entity == null) return false;

        entity.SourceReference = sourceReference;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetRouteActiveAsync(long routeId, bool isActive, CancellationToken ct = default)
    {
        _context.ChangeTracker.Clear();
        var entity = await _context.Routes.FirstOrDefaultAsync(r => r.Id == routeId, ct);
        if (entity == null) return false;

        entity.IsActive = isActive;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public Task<int> CountUpcomingTripsForRouteAsync(long routeId, DateTimeOffset now, CancellationToken ct = default) =>
        _context.Trips.CountAsync(t => t.RouteId == routeId && t.DepartureTime > now &&
                                       t.Status != TripStatus.Cancelled && t.Status != TripStatus.Completed, ct);

    public async Task ReplaceRouteStopsAsync(long routeId, IReadOnlyList<RouteStop> stops, CancellationToken ct = default)
    {
        _context.ChangeTracker.Clear();
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);
            var existing = await _context.RouteStops.Where(s => s.RouteId == routeId).ToListAsync(ct);
            _context.RouteStops.RemoveRange(existing);
            await _context.SaveChangesAsync(ct);
            foreach (var stop in stops) stop.Id = 0;
            _context.RouteStops.AddRange(stops);
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });
        _context.ChangeTracker.Clear();
    }

    public async Task<List<AdminPointDto>> GetPointsAsync(long operatorId, CancellationToken ct = default)
    {
        return await _context.PickupDropoffPoints.AsNoTracking()
            .Where(p => p.OperatorId == operatorId)
            .OrderBy(p => p.Location.Name).ThenBy(p => p.Name)
            .Select(p => new AdminPointDto
            {
                Id = p.Id,
                OperatorId = p.OperatorId,
                LocationId = p.LocationId,
                LocationName = p.Location.Name,
                Name = p.Name,
                Address = p.Address,
                IsActive = p.IsActive
            })
            .ToListAsync(ct);
    }

    public Task<List<long>> GetActivePointIdsAsync(IReadOnlyCollection<long> pointIds, CancellationToken ct = default) =>
        _context.PickupDropoffPoints.Where(p => pointIds.Contains(p.Id) && p.IsActive).Select(p => p.Id).ToListAsync(ct);

    public async Task<long> AddPointAsync(PickupDropoffPoint point, CancellationToken ct = default)
    {
        _context.PickupDropoffPoints.Add(point);
        await _context.SaveChangesAsync(ct);
        return point.Id;
    }

    // ---------------------------------------------------------------- Fleet

    public async Task<List<AdminBusDto>> GetBusesAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        return await _context.Buses.AsNoTracking()
            .OrderByDescending(b => b.IsActive).ThenBy(b => b.LicensePlate)
            .Select(b => new AdminBusDto
            {
                Id = b.Id,
                OperatorId = b.OperatorId,
                OperatorName = b.Operator.Name,
                BusTypeId = b.BusTypeId,
                BusTypeName = b.BusType.Name,
                LicensePlate = b.LicensePlate,
                Name = b.Name,
                IsActive = b.IsActive,
                TotalSeats = b.SeatLayout.TotalSeats,
                Floors = b.SeatLayout.Floors,
                UpcomingTrips = _context.Trips.Count(t => t.BusId == b.Id && t.DepartureTime > now &&
                                                          t.Status != TripStatus.Cancelled && t.Status != TripStatus.Completed)
            })
            .ToListAsync(ct);
    }

    public Task<List<AdminSeatDto>> GetBusSeatsAsync(long busId, CancellationToken ct = default) =>
        _context.Buses.AsNoTracking()
            .Where(b => b.Id == busId)
            .SelectMany(b => b.SeatLayout.Items)
            .Where(item => item.IsActive)
            .OrderBy(item => item.Floor).ThenBy(item => item.Row).ThenBy(item => item.Column)
            .Select(item => new AdminSeatDto
            {
                SeatCode = item.SeatCode,
                Floor = item.Floor,
                Row = item.Row,
                Column = item.Column,
                SeatType = item.SeatType
            })
            .ToListAsync(ct);

    public Task<bool> LicensePlateTakenAsync(string plate, CancellationToken ct = default) =>
        _context.Buses.AnyAsync(b => b.LicensePlate == plate, ct);

    public Task<int> CountUpcomingTripsForBusAsync(long busId, DateTimeOffset now, CancellationToken ct = default) =>
        _context.Trips.CountAsync(t => t.BusId == busId && t.DepartureTime > now &&
                                       t.Status != TripStatus.Cancelled && t.Status != TripStatus.Completed, ct);

    public async Task<bool> ReplaceBusLayoutAsync(long busId, List<SeatLayoutItem> items, CancellationToken ct = default)
    {
        _context.ChangeTracker.Clear();
        var bus = await _context.Buses.FirstOrDefaultAsync(b => b.Id == busId, ct);
        if (bus == null) return false;

        // A new layout row, not an edit: past trips keep pointing at the layout they were sold on.
        var layout = new SeatLayout
        {
            Name = $"Sơ đồ xe {bus.LicensePlate} ({DateTimeOffset.UtcNow:yyyyMMddHHmm})",
            Floors = items.Max(item => item.Floor),
            TotalSeats = items.Count,
            IsActive = true,
            Items = items
        };
        _context.SeatLayouts.Add(layout);
        bus.SeatLayout = layout;
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();
        return true;
    }

    public async Task<bool> SetBusActiveAsync(long busId, bool isActive, CancellationToken ct = default)
    {
        _context.ChangeTracker.Clear();
        var bus = await _context.Buses.FirstOrDefaultAsync(b => b.Id == busId, ct);
        if (bus == null) return false;

        bus.IsActive = isActive;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public Task<List<AdminDriverDto>> GetDriversAsync(CancellationToken ct = default) =>
        _context.Drivers.AsNoTracking()
            .OrderByDescending(d => d.IsActive).ThenBy(d => d.FullName)
            .Select(d => new AdminDriverDto
            {
                Id = d.Id,
                OperatorId = d.OperatorId,
                OperatorName = d.Operator.Name,
                FullName = d.FullName,
                Phone = d.Phone,
                LicenseNumber = d.LicenseNumber,
                LicenseClass = d.LicenseClass,
                LicenseExpiryDate = d.LicenseExpiryDate,
                IsActive = d.IsActive
            })
            .ToListAsync(ct);

    public Task<Driver?> GetDriverAsync(long driverId, CancellationToken ct = default) =>
        _context.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.Id == driverId, ct);

    public Task<bool> DriverLicenseTakenAsync(string licenseNumber, long exceptId, CancellationToken ct = default) =>
        _context.Drivers.AnyAsync(d => d.Id != exceptId && d.LicenseNumber == licenseNumber, ct);

    public async Task<long> SaveDriverAsync(AdminDriverInput input, CancellationToken ct = default)
    {
        _context.ChangeTracker.Clear();
        var driver = input.Id > 0 ? await _context.Drivers.FirstOrDefaultAsync(d => d.Id == input.Id, ct) : null;
        if (driver == null)
        {
            driver = new Driver { CreatedAt = DateTimeOffset.UtcNow, IsActive = true };
            _context.Drivers.Add(driver);
        }

        driver.OperatorId = input.OperatorId;
        driver.FullName = input.FullName;
        driver.Phone = input.Phone;
        driver.LicenseNumber = input.LicenseNumber;
        driver.LicenseClass = input.LicenseClass;
        driver.LicenseExpiryDate = input.LicenseExpiryDate;
        await _context.SaveChangesAsync(ct);
        return driver.Id;
    }

    public async Task<bool> SetDriverActiveAsync(long driverId, bool isActive, CancellationToken ct = default)
    {
        _context.ChangeTracker.Clear();
        var driver = await _context.Drivers.FirstOrDefaultAsync(d => d.Id == driverId, ct);
        if (driver == null) return false;

        driver.IsActive = isActive;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public Task<List<Driver>> GetDriversByIdsAsync(IReadOnlyCollection<long> driverIds, CancellationToken ct = default) =>
        _context.Drivers.AsNoTracking().Where(d => driverIds.Contains(d.Id)).ToListAsync(ct);

    public Task<bool> DriverHasConflictAsync(long driverId, DateTimeOffset departure, DateTimeOffset arrival, long exceptTripId, CancellationToken ct = default) =>
        _context.TripDrivers.AnyAsync(td =>
            td.DriverId == driverId &&
            td.TripId != exceptTripId &&
            td.Trip.Status != TripStatus.Cancelled &&
            td.Trip.Status != TripStatus.Completed &&
            td.Trip.DepartureTime < arrival &&
            td.Trip.ExpectedArrivalTime > departure, ct);

    // ---------------------------------------------------------------- Trips

    public async Task<PagedResult<AdminTripDto>> GetTripsAsync(AdminTripFilter filter, DateTimeOffset now, CancellationToken ct = default)
    {
        var query = _context.Trips.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(t => t.TripCode.Contains(term) || t.Route.Name.Contains(term) ||
                                     t.Route.Operator.Name.Contains(term) || t.Bus.LicensePlate.Contains(term));
        }

        if (Enum.TryParse<TripStatus>(filter.Status, out var status))
        {
            query = query.Where(t => t.Status == status);
        }

        if (filter.UpcomingOnly)
        {
            query = query.Where(t => t.DepartureTime > now);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(t => t.DepartureTime < now).ThenBy(t => t.DepartureTime)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(t => new AdminTripDto
            {
                Id = t.Id,
                TripCode = t.TripCode,
                RouteId = t.RouteId,
                RouteName = t.Route.Name,
                OperatorId = t.Route.OperatorId,
                OperatorName = t.Route.Operator.Name,
                BusId = t.BusId,
                BusLicensePlate = t.Bus.LicensePlate,
                DepartureTime = t.DepartureTime,
                ExpectedArrivalTime = t.ExpectedArrivalTime,
                BasePrice = t.BasePrice,
                Status = t.Status.ToString(),
                IsSandbox = t.IsDemoTrip,
                DataSource = t.DataSource.ToString(),
                TotalSeats = t.Bus.SeatLayout.TotalSeats,
                BookedSeats = t.Seats.Count(s => s.Status == TripSeatStatus.Booked),
                ActiveBookings = t.Bookings.Count(b => b.Status == BookingStatus.PendingPayment || b.Status == BookingStatus.Confirmed),
                DriverIds = t.Drivers.Select(d => d.DriverId).ToList(),
                CancellationReason = t.CancellationReason
            })
            .ToListAsync(ct);

        return new PagedResult<AdminTripDto> { Items = items, Page = filter.Page, PageSize = filter.PageSize, TotalCount = total };
    }

    public Task<Trip?> GetTripAsync(long tripId, CancellationToken ct = default) =>
        _context.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripId, ct);

    public Task<Route?> GetActiveRouteAsync(long routeId, CancellationToken ct = default) =>
        _context.Routes.AsNoTracking()
            .Include(r => r.Operator)
            .Include(r => r.Stops)
            .FirstOrDefaultAsync(r => r.Id == routeId && r.IsActive, ct);

    public Task<Bus?> GetActiveBusWithLayoutAsync(long busId, CancellationToken ct = default) =>
        _context.Buses.AsNoTracking()
            .Include(b => b.SeatLayout).ThenInclude(l => l.Items)
            .FirstOrDefaultAsync(b => b.Id == busId && b.IsActive, ct);

    public Task<bool> BusHasConflictAsync(long busId, DateTimeOffset departure, DateTimeOffset arrival, long exceptTripId, CancellationToken ct = default) =>
        _context.Trips.AnyAsync(t =>
            t.Id != exceptTripId &&
            t.BusId == busId &&
            t.Status != TripStatus.Cancelled &&
            t.Status != TripStatus.Completed &&
            t.DepartureTime < arrival &&
            t.ExpectedArrivalTime > departure, ct);

    public Task<int> CountActiveBookingsAsync(long tripId, CancellationToken ct = default) =>
        _context.Bookings.CountAsync(b => b.TripId == tripId &&
            (b.Status == BookingStatus.PendingPayment || b.Status == BookingStatus.Confirmed), ct);

    public async Task FinalizeNewTripAsync(long tripId, bool openAsSandbox, CancellationToken ct = default)
    {
        _context.ChangeTracker.Clear();
        var trip = await _context.Trips.FirstAsync(t => t.Id == tripId, ct);
        var routeStops = await _context.RouteStops.AsNoTracking()
            .Where(s => s.RouteId == trip.RouteId)
            .OrderBy(s => s.StopOrder)
            .ToListAsync(ct);

        foreach (var stop in routeStops)
        {
            _context.TripStops.Add(new TripStop
            {
                TripId = tripId,
                PointId = stop.PointId,
                StopOrder = stop.StopOrder,
                AllowPickup = stop.AllowPickup,
                AllowDropoff = stop.AllowDropoff,
                ScheduledDepartureTime = stop.AllowPickup && stop.DepartureOffsetMinutes.HasValue
                    ? trip.DepartureTime.AddMinutes(stop.DepartureOffsetMinutes.Value) : null,
                ScheduledArrivalTime = stop.AllowDropoff && stop.ArrivalOffsetMinutes.HasValue
                    ? trip.DepartureTime.AddMinutes(stop.ArrivalOffsetMinutes.Value) : null
            });
        }

        trip.IsDemoTrip = openAsSandbox;
        trip.DataSource = openAsSandbox ? TransportDataSource.SandboxFixture : TransportDataSource.ReferenceSchedule;
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();
    }

    public async Task<bool> UpdateTripScheduleAsync(AdminTripInput input, CancellationToken ct = default)
    {
        _context.ChangeTracker.Clear();
        var trip = await _context.Trips.Include(t => t.Seats).FirstOrDefaultAsync(t => t.Id == input.Id, ct);
        if (trip == null) return false;

        if (trip.BasePrice != input.BasePrice)
        {
            // Keep each seat's surcharge; only the base fare moves.
            var difference = input.BasePrice - trip.BasePrice;
            foreach (var seat in trip.Seats)
            {
                seat.Price += difference;
                seat.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        trip.RouteId = input.RouteId;
        trip.BusId = input.BusId;
        trip.DepartureTime = input.DepartureTime;
        trip.ExpectedArrivalTime = input.ExpectedArrivalTime;
        trip.BasePrice = input.BasePrice;
        trip.UpdatedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();
        return true;
    }

    public async Task ReplaceTripDriversAsync(long tripId, IReadOnlyCollection<long> driverIds, CancellationToken ct = default)
    {
        _context.ChangeTracker.Clear();
        var existing = await _context.TripDrivers.Where(d => d.TripId == tripId).ToListAsync(ct);
        _context.TripDrivers.RemoveRange(existing);
        var index = 0;
        foreach (var driverId in driverIds.Distinct())
        {
            _context.TripDrivers.Add(new TripDriver
            {
                TripId = tripId,
                DriverId = driverId,
                Role = index++ == 0 ? DriverAssignmentRole.Primary : DriverAssignmentRole.Secondary
            });
        }

        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();
    }

    public async Task<bool> SetTripStatusAsync(long tripId, TripStatus status, CancellationToken ct = default)
    {
        _context.ChangeTracker.Clear();
        var trip = await _context.Trips.FirstOrDefaultAsync(t => t.Id == tripId, ct);
        if (trip == null) return false;

        trip.Status = status;
        trip.UpdatedAt = DateTimeOffset.UtcNow;

        if (status == TripStatus.Completed)
        {
            var bookings = await _context.Bookings
                .Where(b => b.TripId == tripId && b.Status == BookingStatus.Confirmed)
                .ToListAsync(ct);
            foreach (var booking in bookings)
            {
                booking.Status = BookingStatus.Completed;
            }
        }

        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();
        return true;
    }

    public async Task<TripCancellationSummary> CancelTripAsync(long tripId, string reason, DateTimeOffset now, CancellationToken ct = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        var summary = await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            var trip = await _context.Trips.Include(t => t.Route).FirstOrDefaultAsync(t => t.Id == tripId, ct);
            if (trip == null) return new TripCancellationSummary { Found = false };
            if (trip.Status == TripStatus.Cancelled) return new TripCancellationSummary { Found = true, AlreadyCancelled = true };

            trip.Status = TripStatus.Cancelled;
            trip.CancellationReason = reason;
            trip.UpdatedAt = now;

            // No new customer can hold these seats: the sale rules now reject the trip. Clear live holds too.
            var holds = await _context.SeatHolds
                .Include(h => h.TripSeat)
                .Where(h => h.TripSeat.TripId == tripId && h.Status == SeatHoldStatus.Active)
                .ToListAsync(ct);
            foreach (var hold in holds)
            {
                hold.Status = SeatHoldStatus.Released;
                if (hold.TripSeat.Status == TripSeatStatus.Held)
                {
                    hold.TripSeat.Status = TripSeatStatus.Available;
                    hold.TripSeat.UpdatedAt = now;
                }
            }

            var bookings = await _context.Bookings
                .Include(b => b.BookingSeats).ThenInclude(s => s.TripSeat)
                .Where(b => b.TripId == tripId && (b.Status == BookingStatus.PendingPayment || b.Status == BookingStatus.Confirmed))
                .ToListAsync(ct);

            var released = 0;
            var paid = new List<long>();
            foreach (var booking in bookings)
            {
                if (booking.PaymentStatus == PaymentStatus.Paid)
                {
                    paid.Add(booking.Id);
                    continue;
                }

                CancelUnpaid(booking, $"Chuyến bị hủy: {reason}", now);
                released++;
            }

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new TripCancellationSummary { Found = true, ReleasedUnpaidBookings = released, PaidBookingIds = paid };
        });

        _context.ChangeTracker.Clear();
        return summary;
    }

    private void CancelUnpaid(Booking booking, string reason, DateTimeOffset now)
    {
        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = now;
        booking.CancellationReason = reason;
        foreach (var seat in booking.BookingSeats)
        {
            seat.IsActive = false;
            if (seat.TripSeat != null)
            {
                seat.TripSeat.Status = TripSeatStatus.Available;
                seat.TripSeat.UpdatedAt = now;
            }
        }
    }

    // ---------------------------------------------------------------- Bookings

    public async Task<PagedResult<AdminBookingRowDto>> GetBookingsAsync(AdminBookingFilter filter, CancellationToken ct = default)
    {
        var query = _context.Bookings.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(b => b.BookingCode.Contains(term) || b.PassengerPhone.Contains(term) || b.Trip.Route.Name.Contains(term));
        }

        if (Enum.TryParse<BookingStatus>(filter.Status, out var status))
        {
            query = query.Where(b => b.Status == status);
        }

        if (Enum.TryParse<PaymentStatus>(filter.PaymentStatus, out var paymentStatus))
        {
            query = query.Where(b => b.PaymentStatus == paymentStatus);
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(b => b.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(b => new
            {
                b.Id,
                b.BookingCode,
                RouteName = b.Trip.Route.Name,
                b.Trip.DepartureTime,
                b.PassengerName,
                b.PassengerPhone,
                b.Status,
                b.PaymentStatus,
                b.TotalAmount,
                b.CreatedAt,
                b.IsDemoBooking
            })
            .ToListAsync(ct);

        return new PagedResult<AdminBookingRowDto>
        {
            Page = filter.Page,
            PageSize = filter.PageSize,
            TotalCount = total,
            Items = rows.Select(b => new AdminBookingRowDto
            {
                Id = b.Id,
                BookingCode = b.BookingCode,
                RouteName = b.RouteName,
                DepartureTime = b.DepartureTime,
                MaskedPassengerName = PrivacyMask.Name(b.PassengerName),
                MaskedPhone = PrivacyMask.Phone(b.PassengerPhone),
                Status = b.Status.ToString(),
                PaymentStatus = b.PaymentStatus.ToString(),
                TotalAmount = b.TotalAmount,
                CreatedAt = b.CreatedAt,
                IsSandbox = b.IsDemoBooking
            }).ToList()
        };
    }

    public async Task<AdminBookingDetailDto?> GetBookingAsync(long bookingId, bool revealContact, CancellationToken ct = default)
    {
        var b = await _context.Bookings.AsNoTracking().AsSplitQuery()
            .Include(x => x.Trip).ThenInclude(t => t.Route).ThenInclude(r => r.Operator)
            .Include(x => x.BookingSeats)
            .Include(x => x.Passengers)
            .Include(x => x.PaymentTransactions)
            .FirstOrDefaultAsync(x => x.Id == bookingId, ct);
        if (b == null) return null;

        return new AdminBookingDetailDto
        {
            Id = b.Id,
            BookingCode = b.BookingCode,
            RouteName = b.Trip.Route.Name,
            OperatorName = b.Trip.Route.Operator.Name,
            DepartureTime = b.Trip.DepartureTime,
            PickupPoint = b.PickupPointNote,
            DropoffPoint = b.DropoffPointNote,
            SeatCodes = b.BookingSeats.Select(s => s.SeatCode).OrderBy(s => s).ToList(),
            MaskedPassengerName = PrivacyMask.Name(b.PassengerName),
            MaskedPhone = PrivacyMask.Phone(b.PassengerPhone),
            MaskedEmail = PrivacyMask.Email(b.PassengerEmail),
            PassengerName = revealContact ? b.PassengerName : null,
            PassengerPhone = revealContact ? b.PassengerPhone : null,
            PassengerEmail = revealContact ? b.PassengerEmail : null,
            ContactRevealed = revealContact,
            PassengerCount = b.Passengers.Count,
            BaseAmount = b.BaseAmount,
            SurchargeAmount = b.SurchargeAmount,
            TotalAmount = b.TotalAmount,
            Status = b.Status.ToString(),
            PaymentStatus = b.PaymentStatus.ToString(),
            IsSandbox = b.IsDemoBooking,
            CreatedAt = b.CreatedAt,
            ExpiresAt = b.ExpiresAt,
            CancelledAt = b.CancelledAt,
            CancellationReason = b.CancellationReason,
            Payments = b.PaymentTransactions.OrderByDescending(t => t.CreatedAt).Select(t => new AdminPaymentRowDto
            {
                TransactionCode = t.TransactionCode,
                Provider = t.Provider,
                PaymentMethod = t.PaymentMethod,
                Amount = t.Amount,
                Currency = t.Currency,
                Status = t.Status.ToString(),
                IsSandbox = t.IsSandbox,
                CreatedAt = t.CreatedAt,
                CompletedAt = t.CompletedAt,
                RefundedAt = t.RefundedAt
            }).ToList()
        };
    }

    public async Task<AdminResult> CancelUnpaidBookingAsync(long bookingId, string reason, DateTimeOffset now, CancellationToken ct = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var booking = await _context.Bookings
                .Include(b => b.BookingSeats).ThenInclude(s => s.TripSeat)
                .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
            if (booking == null) return AdminResult.Fail("Không tìm thấy đơn đặt chỗ.");
            if (booking.Status != BookingStatus.PendingPayment || booking.PaymentStatus == PaymentStatus.Paid)
                return AdminResult.Fail("Chỉ hủy trực tiếp được đơn chưa thanh toán.");

            CancelUnpaid(booking, reason, now);
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return AdminResult.Ok("Đã hủy đơn và trả ghế về trạng thái trống.");
        });

        _context.ChangeTracker.Clear();
        return result;
    }

    // ---------------------------------------------------------------- Monitoring

    public async Task<PagedResult<AdminAuditRowDto>> GetAuditLogAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var total = await _context.AdminAuditLogs.CountAsync(ct);
        var items = await _context.AdminAuditLogs.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new AdminAuditRowDto
            {
                Id = a.Id,
                ActorEmail = a.ActorEmail,
                Action = a.Action,
                EntityType = a.EntityType,
                EntityId = a.EntityId,
                Details = a.Details,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync(ct);
        return new PagedResult<AdminAuditRowDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = total };
    }

    public async Task<PagedResult<AdminEmailRowDto>> GetEmailOutboxAsync(string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _context.BookingConfirmationEmailOutbox.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(e => e.Status == status);
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new
            {
                e.Id,
                e.Booking.BookingCode,
                e.MessageType,
                e.RecipientEmail,
                e.Status,
                e.AttemptCount,
                e.NextAttemptAt,
                e.SentAt,
                e.LastError,
                e.Payload
            })
            .ToListAsync(ct);

        return new PagedResult<AdminEmailRowDto>
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            Items = rows.Select(e => new AdminEmailRowDto
            {
                Id = e.Id,
                BookingCode = e.BookingCode,
                MessageType = e.MessageType,
                MaskedRecipient = PrivacyMask.Email(e.RecipientEmail),
                Status = e.Status,
                AttemptCount = e.AttemptCount,
                NextAttemptAt = e.NextAttemptAt,
                SentAt = e.SentAt,
                LastError = e.LastError,
                RecipientEmail = e.RecipientEmail,
                Payload = e.Payload
            }).ToList()
        };
    }

    public async Task<bool> RetryFailedEmailAsync(long outboxId, DateTimeOffset now, CancellationToken ct = default)
    {
        _context.ChangeTracker.Clear();
        var message = await _context.BookingConfirmationEmailOutbox.FirstOrDefaultAsync(e => e.Id == outboxId, ct);
        if (message == null || message.Status != BookingConfirmationEmailOutbox.Failed) return false;

        message.Status = BookingConfirmationEmailOutbox.Pending;
        message.AttemptCount = 0;
        message.NextAttemptAt = now;
        message.LockedUntil = null;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<PagedResult<AdminPaymentEventDto>> GetPaymentEventsAsync(bool needsReviewOnly, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _context.PaymentWebhookEvents.AsNoTracking();
        if (needsReviewOnly)
        {
            var outcomes = NeedsReviewOutcomes;
            query = query.Where(e => outcomes.Contains(e.Outcome));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(e => e.ReceivedAt).ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new AdminPaymentEventDto
            {
                Id = e.Id,
                Provider = e.Provider,
                EventId = e.EventId,
                TransactionCode = e.TransactionCode,
                SignatureValid = e.SignatureValid,
                Outcome = e.Outcome,
                ReportedAmount = e.ReportedAmount,
                ReportedCurrency = e.ReportedCurrency,
                Detail = e.Detail,
                ReceivedAt = e.ReceivedAt
            })
            .ToListAsync(ct);
        return new PagedResult<AdminPaymentEventDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = total };
    }
}

internal static class PrivacyMask
{
    public static string Name(string? name)
    {
        var parts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? string.Empty : string.Join(' ', parts.Select(part => part[0] + "***"));
    }

    public static string Phone(string? phone)
    {
        var value = phone ?? string.Empty;
        return value.Length <= 4 ? new string('*', value.Length) : new string('*', value.Length - 3) + value[^3..];
    }

    public static string Email(string? email)
    {
        var value = email ?? string.Empty;
        var at = value.IndexOf('@');
        return at <= 0 ? string.Empty : value[0] + "***" + value[at..];
    }
}
