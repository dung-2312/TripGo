using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tripgo.Application.Interfaces;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;
using Tripgo.Infrastructure.Persistence;

using Tripgo.Application.Services;

namespace Tripgo.Infrastructure.Services;

public class SandboxFixtureService : ISandboxFixtureService
{
    private readonly AppDbContext _context;
    private readonly ILogger<SandboxFixtureService> _logger;
    private readonly IPasswordHasherService _passwordHasher;

    public SandboxFixtureService(
        AppDbContext context,
        ILogger<SandboxFixtureService> logger,
        IPasswordHasherService? passwordHasher = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _passwordHasher = passwordHasher ?? new PasswordHasherService();
    }

    public async Task EnsureSandboxTripsAsync(CancellationToken ct = default)
    {
        var dbConnection = _context.Database.GetDbConnection();
        var dbName = dbConnection.Database;

        // STRICT DATABASE SAFETY: Strictly forbid any write to production database TripgoDb
        if (string.Equals(dbName, "TripgoDb", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(dbName, "TripgoDb_IsolatedTest", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Sandbox fixture creation is strictly forbidden against non-test database {Database}.", dbName);
            throw new InvalidOperationException($"Sandbox fixture creation is permitted ONLY against TripgoDb_IsolatedTest. Target database '{dbName}' was rejected.");
        }

        try
        {
            _logger.LogInformation("Beginning dynamic sandbox trip and route stop preparation in {Database}...", dbName);

            // 0. Ensure default roles and users exist, repair pickup/dropoff Unicode text, and deduplicate locations
            await EnsureRolesAndDefaultUsersAsync(ct);
            await EnsureFreshDatabaseBaselineAsync(ct);
            await RepairPickupDropoffUnicodeAsync(ct);
            await EnsureCanonicalLocationsAsync(ct);

            // 1. Ensure nationwide corridor routes (e.g. HAN <-> DAD) are active for complete test coverage
            var routesToActivate = await _context.Routes
                .Where(r => (r.Code == "FUTA-HAN-DAD" || r.Code == "FUTA-DAD-HAN") && !r.IsActive)
                .ToListAsync(ct);

            if (routesToActivate.Count > 0)
            {
                foreach (var r in routesToActivate)
                {
                    r.IsActive = true;
                }
                await _context.SaveChangesAsync(ct);
                _logger.LogInformation("Activated {Count} nationwide corridor routes in {Database}.", routesToActivate.Count, dbName);
            }

            // 2. Discover all imported active routes dynamically
            var activeRoutes = await _context.Routes
                .Include(r => r.Operator)
                .Include(r => r.OriginLocation)
                .Include(r => r.DestinationLocation)
                .Include(r => r.Stops)
                    .ThenInclude(s => s.Point)
                .Where(r => r.IsActive)
                .OrderBy(r => r.Id)
                .ToListAsync(ct);

            _logger.LogInformation("Discovered {Count} active imported routes to audit for sandbox booking coverage.", activeRoutes.Count);

            foreach (var route in activeRoutes)
            {
                await EnsureRouteCoverageAsync(route, ct);
            }

            _logger.LogInformation("Sandbox fixture synchronization completed successfully for all {Count} imported routes in {Database}.", activeRoutes.Count, dbName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure sandbox trip fixtures across imported routes in {Database}.", dbName);
            throw;
        }
    }

    /// <summary>
    /// A freshly migrated isolated database has no transport data, and the imported data set is not
    /// in the repository. This creates a self-evidently fictional operator, bus and route network so
    /// the booking journey can run end to end. Nothing here claims to describe a real operator, fare
    /// or timetable, and it never runs when any route already exists.
    /// </summary>
    public async Task EnsureFreshDatabaseBaselineAsync(CancellationToken ct = default)
    {
        _context.EnsureWritesTargetIsolatedTestDatabase();

        if (await _context.Routes.AnyAsync(ct))
        {
            return;
        }

        await DbInitializer.SeedAsync(_context);

        // Point IDs 1-30 are reserved for the imported data set that RepairPickupDropoffUnicodeAsync
        // corrects by ID. Keep sandbox points out of that range so they are never renamed by it.
        if (!await _context.PickupDropoffPoints.AnyAsync(ct))
        {
            await _context.Database.ExecuteSqlRawAsync(
                "DBCC CHECKIDENT ('[PickupDropoffPoints]', RESEED, 1000) WITH NO_INFOMSGS;", ct);
        }

        var sandboxOperator = await _context.Operators.FirstOrDefaultAsync(o => o.Code == SandboxOperatorCode, ct);
        if (sandboxOperator == null)
        {
            sandboxOperator = new Operator
            {
                Code = SandboxOperatorCode,
                Name = "[SANDBOX] Nhà xe thử nghiệm",
                Description = "Nhà xe hư cấu chỉ dùng để kiểm thử. Không phải đơn vị vận tải có thật.",
                DataSource = TransportDataSource.SandboxFixture,
                IsActive = true,
                IsVerified = false,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _context.Operators.Add(sandboxOperator);
            await _context.SaveChangesAsync(ct);
        }

        if (!await _context.Buses.AnyAsync(b => b.OperatorId == sandboxOperator.Id, ct))
        {
            var busType = await _context.BusTypes.OrderBy(t => t.Id).FirstAsync(ct);
            const int seatsPerFloor = 12;
            var items = new List<SeatLayoutItem>();
            for (var floor = 1; floor <= 2; floor++)
            {
                for (var position = 1; position <= seatsPerFloor; position++)
                {
                    items.Add(new SeatLayoutItem
                    {
                        SeatCode = $"{(floor == 1 ? 'A' : 'B')}{position:D2}",
                        Floor = floor,
                        Row = (position - 1) / 2 + 1,
                        Column = (position - 1) % 2 + 1,
                        SeatType = "Bed",
                        IsActive = true
                    });
                }
            }

            _context.Buses.Add(new Bus
            {
                OperatorId = sandboxOperator.Id,
                BusTypeId = busType.Id,
                LicensePlate = "SANDBOX-001",
                Name = "[SANDBOX] Xe thử nghiệm 01",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                SeatLayout = new SeatLayout
                {
                    Name = "[SANDBOX] Sơ đồ 24 giường",
                    Floors = 2,
                    TotalSeats = items.Count,
                    IsActive = true,
                    Items = items
                }
            });
            await _context.SaveChangesAsync(ct);
        }

        var locations = await _context.Locations.ToDictionaryAsync(l => l.Code, StringComparer.OrdinalIgnoreCase, ct);
        var pairs = new[]
        {
            ("SGN", "DLI"), ("SGN", "NHA"), ("SGN", "UIH"), ("SGN", "DAD"),
            ("HAN", "SAP"), ("HAN", "DAD"), ("DAD", "NHA")
        };

        foreach (var (first, second) in pairs)
        {
            foreach (var (from, to) in new[] { (first, second), (second, first) })
            {
                _context.Routes.Add(new Route
                {
                    OperatorId = sandboxOperator.Id,
                    OriginLocationId = locations[from].Id,
                    DestinationLocationId = locations[to].Id,
                    Code = $"SANDBOX-{from}-{to}",
                    Name = $"[SANDBOX] {locations[from].Name} - {locations[to].Name}",
                    DataSource = TransportDataSource.SandboxFixture,
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
        }

        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Created the fictional sandbox baseline in a fresh isolated database.");
    }

    public const string SandboxOperatorCode = "SANDBOX";

    private async Task EnsureCanonicalLocationsAsync(CancellationToken ct)
    {
        // Consolidate legacy location 1 (HN) into verified canonical 10002 (HAN)
        var loc1 = await _context.Locations.FindAsync(new object[] { 1L }, ct);
        var locHan = await _context.Locations.FirstOrDefaultAsync(l => l.Code == "HAN" && l.Id != 1L, ct);
        if (loc1 != null && locHan != null)
        {
            var routesOrigin = await _context.Routes.Where(r => r.OriginLocationId == 1L).ToListAsync(ct);
            foreach (var r in routesOrigin) r.OriginLocationId = locHan.Id;

            var routesDest = await _context.Routes.Where(r => r.DestinationLocationId == 1L).ToListAsync(ct);
            foreach (var r in routesDest) r.DestinationLocationId = locHan.Id;

            var points = await _context.PickupDropoffPoints.Where(p => p.LocationId == 1L).ToListAsync(ct);
            foreach (var p in points) p.LocationId = locHan.Id;

            loc1.IsActive = false;
        }

        // Consolidate legacy location 2 (HCM) into verified canonical 10003 (SGN)
        var loc2 = await _context.Locations.FindAsync(new object[] { 2L }, ct);
        var locSgn = await _context.Locations.FirstOrDefaultAsync(l => l.Code == "SGN" && l.Id != 2L, ct);
        if (loc2 != null && locSgn != null)
        {
            var routesOrigin = await _context.Routes.Where(r => r.OriginLocationId == 2L).ToListAsync(ct);
            foreach (var r in routesOrigin) r.OriginLocationId = locSgn.Id;

            var routesDest = await _context.Routes.Where(r => r.DestinationLocationId == 2L).ToListAsync(ct);
            foreach (var r in routesDest) r.DestinationLocationId = locSgn.Id;

            var points = await _context.PickupDropoffPoints.Where(p => p.LocationId == 2L).ToListAsync(ct);
            foreach (var p in points) p.LocationId = locSgn.Id;

            loc2.IsActive = false;
        }

        await _context.SaveChangesAsync(ct);
    }

    private async Task EnsureRouteCoverageAsync(Route route, CancellationToken ct)
    {
        // 1. Ensure valid pickup and dropoff points exist in RouteStops
        await EnsureRouteStopsAsync(route, ct);

        // Reload route stops after potential additions
        var routeStops = await _context.RouteStops
            .Include(rs => rs.Point)
            .Where(rs => rs.RouteId == route.Id)
            .OrderBy(rs => rs.StopOrder)
            .ToListAsync(ct);

        // 2. Resolve an active bus belonging to the route's operator (or fallback active bus)
        var bus = await _context.Buses
            .Include(b => b.SeatLayout)
                .ThenInclude(sl => sl.Items)
            .FirstOrDefaultAsync(b => b.OperatorId == route.OperatorId && b.IsActive && b.SeatLayout != null && b.SeatLayout.Items.Any(), ct);

        if (bus == null)
        {
            bus = await _context.Buses
                .Include(b => b.SeatLayout)
                    .ThenInclude(sl => sl.Items)
                .FirstOrDefaultAsync(b => b.IsActive && b.SeatLayout != null && b.SeatLayout.Items.Any(), ct);
        }

        if (bus == null)
        {
            _logger.LogWarning("No active bus with seat layout found for route {RouteCode}. Skipping trip creation.", route.Code);
            return;
        }

        var durationMinutes = route.EstimatedDurationMinutes ?? 360;
        var basePrice = CalculateSandboxBasePrice(route);

        // 3. Define rolling future test departures (14 days ahead in UTC+7)
        var vnOffset = TimeSpan.FromHours(7);
        var now = DateTimeOffset.UtcNow.ToOffset(vnOffset);
        var today = now.Date;

        var shiftsToEnsure = new List<(string TripCode, DateTimeOffset DepartureTime)>();

        // Legacy shifts for backward compatibility
        var legacyToday = now.TimeOfDay < new TimeSpan(21, 0, 0)
            ? new DateTimeOffset(today.AddHours(22).AddMinutes(30), vnOffset)
            : now.AddHours(2);
        var legacyTomorrowMorning = new DateTimeOffset(today.AddDays(1).AddHours(8), vnOffset);
        var legacyTomorrowEvening = new DateTimeOffset(today.AddDays(1).AddHours(20), vnOffset);

        shiftsToEnsure.Add(($"DEMO-SANDBOX-{route.Code}-TODAY", legacyToday));
        shiftsToEnsure.Add(($"DEMO-SANDBOX-{route.Code}-TOMORROW-0800", legacyTomorrowMorning));
        shiftsToEnsure.Add(($"DEMO-SANDBOX-{route.Code}-TOMORROW-2000", legacyTomorrowEvening));

        // Rolling 14-day schedule: 3 daily shifts (08:00 morning, 14:00 afternoon, 20:30 evening)
        var dailyTimes = new (int Hour, int Minute)[] { (8, 0), (14, 0), (20, 30) };
        for (int day = 0; day <= 14; day++)
        {
            var targetDay = today.AddDays(day);
            foreach (var (hour, minute) in dailyTimes)
            {
                var depTime = new DateTimeOffset(targetDay.AddHours(hour).AddMinutes(minute), vnOffset);
                if (depTime > now.AddMinutes(15))
                {
                    var code = $"[SANDBOX]-{route.Code}-{depTime:yyMMdd}-{depTime:HHmm}";
                    shiftsToEnsure.Add((code, depTime));
                }
            }
        }

        // 3. Batch check existing trips on this route to avoid redundant queries
        var existingTripCodes = (await _context.Trips
            .Where(t => t.RouteId == route.Id)
            .Select(t => t.TripCode)
            .ToListAsync(ct)).ToHashSet();

        var newTrips = new List<Trip>();
        var layoutItems = bus.SeatLayout?.Items?.Where(i => i.IsActive).ToList() ?? new List<SeatLayoutItem>();
        var createdTimestamp = DateTimeOffset.UtcNow;

        foreach (var (tripCode, depTime) in shiftsToEnsure)
        {
            if (existingTripCodes.Contains(tripCode))
                continue;

            var stops = new List<TripStop>();
            foreach (var rs in routeStops.OrderBy(s => s.StopOrder))
            {
                stops.Add(new TripStop
                {
                    PointId = rs.PointId,
                    StopOrder = rs.StopOrder,
                    AllowPickup = rs.AllowPickup,
                    AllowDropoff = rs.AllowDropoff,
                    ScheduledDepartureTime = rs.AllowPickup ? depTime.AddMinutes(rs.DepartureOffsetMinutes ?? 0) : null,
                    ScheduledArrivalTime = rs.AllowDropoff ? depTime.AddMinutes(rs.ArrivalOffsetMinutes ?? durationMinutes) : null
                });
            }

            var seats = new List<TripSeat>();
            foreach (var item in layoutItems)
            {
                var isCouple = string.Equals(item.SeatType, "DoubleBed", StringComparison.OrdinalIgnoreCase);
                var seatPrice = basePrice + (isCouple ? 100_000m : 0m);
                seats.Add(new TripSeat
                {
                    SeatLayoutItemId = item.Id,
                    SeatCode = item.SeatCode,
                    SeatType = item.SeatType,
                    Floor = item.Floor,
                    Row = item.Row,
                    Column = item.Column,
                    Price = seatPrice,
                    Status = TripSeatStatus.Available,
                    CreatedAt = createdTimestamp
                });
            }

            newTrips.Add(new Trip
            {
                RouteId = route.Id,
                BusId = bus.Id,
                TripCode = tripCode,
                DepartureTime = depTime,
                ExpectedArrivalTime = depTime.AddMinutes(durationMinutes),
                BasePrice = basePrice,
                Status = TripStatus.Scheduled,
                IsDemoTrip = true,
                DataSource = TransportDataSource.SandboxFixture,
                CreatedAt = createdTimestamp,
                Stops = stops,
                Seats = seats
            });
        }

        if (newTrips.Count > 0)
        {
            await _context.Trips.AddRangeAsync(newTrips, ct);
            await _context.SaveChangesAsync(ct);
            _context.ChangeTracker.Clear();
            _logger.LogInformation("Batch-created {Count} sandbox trips for route {RouteCode}.", newTrips.Count, route.Code);
        }

        // 4. Roll forward legacy TODAY/TOMORROW trips if in the past
        var legacyToRoll = await _context.Trips
            .Where(t => t.RouteId == route.Id && (t.TripCode.EndsWith("-TODAY") || t.TripCode.Contains("-TOMORROW-")) && t.DepartureTime < DateTimeOffset.Now)
            .ToListAsync(ct);
        if (legacyToRoll.Count > 0)
        {
            foreach (var lt in legacyToRoll)
            {
                lt.DepartureTime = legacyTomorrowMorning;
                lt.ExpectedArrivalTime = legacyTomorrowMorning.AddMinutes(durationMinutes);
                lt.Status = TripStatus.Scheduled;
                lt.IsDemoTrip = true;
            }
            await _context.SaveChangesAsync(ct);
            _context.ChangeTracker.Clear();
        }

        // 5. Ensure existing upcoming demo trips have available seats for testing
        var upcomingTripsWithoutSeats = await _context.Trips
            .Include(t => t.Seats)
            .Where(t => t.RouteId == route.Id && t.IsDemoTrip && t.Status == TripStatus.Scheduled && t.DepartureTime > DateTimeOffset.UtcNow)
            .ToListAsync(ct);

        var restoredSeats = false;
        foreach (var trip in upcomingTripsWithoutSeats)
        {
            if (trip.Seats.Count > 0 && !trip.Seats.Any(s => s.Status == TripSeatStatus.Available))
            {
                foreach (var s in trip.Seats)
                {
                    s.Status = TripSeatStatus.Available;
                }
                restoredSeats = true;
            }
        }
        if (restoredSeats)
        {
            await _context.SaveChangesAsync(ct);
            _context.ChangeTracker.Clear();
        }
    }

    private async Task EnsureRouteStopsAsync(Route route, CancellationToken ct)
    {
        var existingStops = await _context.RouteStops
            .Include(rs => rs.Point)
            .Where(rs => rs.RouteId == route.Id)
            .OrderBy(rs => rs.StopOrder)
            .ToListAsync(ct);

        var hasPickups = existingStops.Any(s => s.AllowPickup && s.Point != null);
        var hasDropoffs = existingStops.Any(s => s.AllowDropoff && s.Point != null);

        if (hasPickups && hasDropoffs)
        {
            return;
        }

        _logger.LogInformation("Route {RouteCode} lacks complete pickup/dropoff stops. Initializing stops...", route.Code);

        // 1. Resolve Origin Pickup Points
        var pickupPoints = await _context.PickupDropoffPoints
            .Where(p => p.LocationId == route.OriginLocationId && p.OperatorId == route.OperatorId && p.IsActive)
            .ToListAsync(ct);

        if (pickupPoints.Count == 0)
        {
            pickupPoints = await _context.PickupDropoffPoints
                .Where(p => p.LocationId == route.OriginLocationId && p.IsActive)
                .ToListAsync(ct);
        }

        if (pickupPoints.Count == 0)
        {
            var simPoint = new PickupDropoffPoint
            {
                OperatorId = route.OperatorId,
                LocationId = route.OriginLocationId,
                Name = $"[SANDBOX] Điểm đón mô phỏng - {route.OriginLocation.Name}",
                Address = $"{route.OriginLocation.Name} (Vị trí đón mô phỏng thử nghiệm)",
                PointType = PointType.Both,
                IsActive = true
            };
            _context.PickupDropoffPoints.Add(simPoint);
            await _context.SaveChangesAsync(ct);
            pickupPoints.Add(simPoint);
        }

        // 2. Resolve Destination Dropoff Points
        var dropoffPoints = await _context.PickupDropoffPoints
            .Where(p => p.LocationId == route.DestinationLocationId && p.OperatorId == route.OperatorId && p.IsActive)
            .ToListAsync(ct);

        if (dropoffPoints.Count == 0)
        {
            dropoffPoints = await _context.PickupDropoffPoints
                .Where(p => p.LocationId == route.DestinationLocationId && p.IsActive)
                .ToListAsync(ct);
        }

        if (dropoffPoints.Count == 0)
        {
            var simPoint = new PickupDropoffPoint
            {
                OperatorId = route.OperatorId,
                LocationId = route.DestinationLocationId,
                Name = $"[SANDBOX] Điểm trả mô phỏng - {route.DestinationLocation.Name}",
                Address = $"{route.DestinationLocation.Name} (Vị trí trả mô phỏng thử nghiệm)",
                PointType = PointType.Both,
                IsActive = true
            };
            _context.PickupDropoffPoints.Add(simPoint);
            await _context.SaveChangesAsync(ct);
            dropoffPoints.Add(simPoint);
        }

        // Clear incomplete existing route stops to maintain strict ordering
        if (existingStops.Count > 0)
        {
            _context.RouteStops.RemoveRange(existingStops);
            route.Stops.Clear();
            await _context.SaveChangesAsync(ct);
        }

        var order = 1;
        var duration = route.EstimatedDurationMinutes ?? 360;
        var newStops = new List<RouteStop>();

        // Add Pickups first
        foreach (var pt in pickupPoints.Take(3))
        {
            newStops.Add(new RouteStop
            {
                RouteId = route.Id,
                PointId = pt.Id,
                StopOrder = order++,
                AllowPickup = true,
                AllowDropoff = false,
                DepartureOffsetMinutes = (order == 2) ? 0 : null,
                ArrivalOffsetMinutes = null
            });
        }

        // Add Dropoffs second
        var dropoffList = dropoffPoints.Take(3).ToList();
        for (int i = 0; i < dropoffList.Count; i++)
        {
            var pt = dropoffList[i];
            var isLast = (i == dropoffList.Count - 1);
            newStops.Add(new RouteStop
            {
                RouteId = route.Id,
                PointId = pt.Id,
                StopOrder = order++,
                AllowPickup = false,
                AllowDropoff = true,
                DepartureOffsetMinutes = null,
                ArrivalOffsetMinutes = isLast ? duration : null
            });
        }

        await _context.RouteStops.AddRangeAsync(newStops, ct);
        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Count} ordered RouteStops for route {RouteCode}.", newStops.Count, route.Code);
    }

    private async Task EnsureSandboxTripAsync(
        Route route,
        Bus bus,
        string tripCode,
        DateTimeOffset departureTime,
        int durationMinutes,
        decimal basePrice,
        List<RouteStop> routeStops,
        CancellationToken ct)
    {
        var trip = await _context.Trips
            .Include(t => t.Stops)
            .Include(t => t.Seats)
            .FirstOrDefaultAsync(t => t.TripCode == tripCode, ct);

        var now = DateTimeOffset.UtcNow;
        if (trip == null)
        {
            var initialStops = new List<TripStop>();
            foreach (var rs in routeStops.OrderBy(s => s.StopOrder))
            {
                initialStops.Add(new TripStop
                {
                    PointId = rs.PointId,
                    StopOrder = rs.StopOrder,
                    AllowPickup = rs.AllowPickup,
                    AllowDropoff = rs.AllowDropoff,
                    ScheduledDepartureTime = rs.AllowPickup ? departureTime.AddMinutes(rs.DepartureOffsetMinutes ?? 0) : null,
                    ScheduledArrivalTime = rs.AllowDropoff ? departureTime.AddMinutes(rs.ArrivalOffsetMinutes ?? durationMinutes) : null
                });
            }

            var layoutItems = bus.SeatLayout?.Items?.Where(i => i.IsActive).ToList() ?? new List<SeatLayoutItem>();
            var initialSeats = new List<TripSeat>();
            foreach (var item in layoutItems)
            {
                var isCouple = string.Equals(item.SeatType, "DoubleBed", StringComparison.OrdinalIgnoreCase);
                var seatPrice = basePrice + (isCouple ? 100_000m : 0m);
                initialSeats.Add(new TripSeat
                {
                    SeatLayoutItemId = item.Id,
                    SeatCode = item.SeatCode,
                    SeatType = item.SeatType,
                    Floor = item.Floor,
                    Row = item.Row,
                    Column = item.Column,
                    Price = seatPrice,
                    Status = TripSeatStatus.Available,
                    CreatedAt = now
                });
            }

            trip = new Trip
            {
                RouteId = route.Id,
                BusId = bus.Id,
                TripCode = tripCode,
                DepartureTime = departureTime,
                ExpectedArrivalTime = departureTime.AddMinutes(durationMinutes),
                BasePrice = basePrice,
                Status = TripStatus.Scheduled,
                IsDemoTrip = true,
                CreatedAt = now,
                Stops = initialStops,
                Seats = initialSeats
            };

            _context.Trips.Add(trip);
            await _context.SaveChangesAsync(ct);
            _context.ChangeTracker.Clear();
            return;
        }
        else
        {
            // For legacy relative shifts, roll forward if past
            if (tripCode.EndsWith("-TODAY") || tripCode.Contains("-TOMORROW-"))
            {
                if (trip.DepartureTime < DateTimeOffset.Now)
                {
                    trip.DepartureTime = departureTime;
                    trip.ExpectedArrivalTime = departureTime.AddMinutes(durationMinutes);
                    trip.Status = TripStatus.Scheduled;
                    trip.IsDemoTrip = true;
                    trip.UpdatedAt = now;
                    await _context.SaveChangesAsync(ct);
                }
            }
        }

        // Ensure TripStops are populated if missing
        if (trip.Stops == null || trip.Stops.Count == 0)
        {
            await CreateTripStopsAsync(trip, routeStops, durationMinutes, ct);
        }

        // Ensure TripSeats inventory is populated if missing
        if (trip.Seats == null || trip.Seats.Count == 0)
        {
            await CreateTripSeatsAsync(trip, bus, ct);
        }

        _context.ChangeTracker.Clear();
    }

    private async Task CreateTripStopsAsync(Trip trip, List<RouteStop> routeStops, int durationMinutes, CancellationToken ct)
    {
        if (routeStops.Count == 0) return;

        var existingPointIds = await _context.TripStops
            .Where(ts => ts.TripId == trip.Id)
            .Select(ts => ts.PointId)
            .ToListAsync(ct);

        var tripStops = new List<TripStop>();
        foreach (var rs in routeStops.OrderBy(s => s.StopOrder))
        {
            if (existingPointIds.Contains(rs.PointId))
                continue;

            tripStops.Add(new TripStop
            {
                TripId = trip.Id,
                PointId = rs.PointId,
                StopOrder = rs.StopOrder,
                AllowPickup = rs.AllowPickup,
                AllowDropoff = rs.AllowDropoff,
                ScheduledDepartureTime = rs.AllowPickup
                    ? trip.DepartureTime.AddMinutes(rs.DepartureOffsetMinutes ?? 0)
                    : null,
                ScheduledArrivalTime = rs.AllowDropoff
                    ? trip.DepartureTime.AddMinutes(rs.ArrivalOffsetMinutes ?? durationMinutes)
                    : null
            });
        }

        if (tripStops.Count > 0)
        {
            await _context.TripStops.AddRangeAsync(tripStops, ct);
            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("Synchronized {Count} TripStops for trip {TripCode}.", tripStops.Count, trip.TripCode);
        }
    }

    private async Task CreateTripSeatsAsync(Trip trip, Bus bus, CancellationToken ct)
    {
        var existingSeatCodes = await _context.TripSeats
            .Where(s => s.TripId == trip.Id)
            .Select(s => s.SeatCode)
            .ToListAsync(ct);

        var layoutItems = bus.SeatLayout?.Items?.Where(i => i.IsActive).ToList() ?? new List<SeatLayoutItem>();
        if (layoutItems.Count == 0) return;

        var newSeats = new List<TripSeat>();
        var now = DateTimeOffset.UtcNow;
        foreach (var item in layoutItems)
        {
            if (existingSeatCodes.Contains(item.SeatCode, StringComparer.OrdinalIgnoreCase))
                continue;

            var isCouple = string.Equals(item.SeatType, "DoubleBed", StringComparison.OrdinalIgnoreCase);
            var seatPrice = trip.BasePrice + (isCouple ? 100_000m : 0m);

            newSeats.Add(new TripSeat
            {
                TripId = trip.Id,
                SeatLayoutItemId = item.Id,
                SeatCode = item.SeatCode,
                SeatType = item.SeatType,
                Floor = item.Floor,
                Row = item.Row,
                Column = item.Column,
                Price = seatPrice,
                Status = TripSeatStatus.Available,
                CreatedAt = now
            });
        }

        if (newSeats.Count > 0)
        {
            await _context.TripSeats.AddRangeAsync(newSeats, ct);
            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded {Count} available seats for trip {TripCode}.", newSeats.Count, trip.TripCode);
        }
    }

    private static decimal CalculateSandboxBasePrice(Route route)
    {
        if (!route.DistanceKm.HasValue || route.DistanceKm.Value <= 0)
        {
            return 350_000m;
        }

        var km = route.DistanceKm.Value;
        if (km <= 350m) return 320_000m;
        if (km <= 500m) return 420_000m;
        if (km <= 700m) return 480_000m;
        return 750_000m;
    }

    public async Task EnsureRolesAndDefaultUsersAsync(CancellationToken ct = default)
    {
        _context.EnsureWritesTargetIsolatedTestDatabase();

        // 1. Roles: Admin, Customer, Operator
        var existingRoles = await _context.Roles.ToListAsync(ct);

        var adminRole = existingRoles.FirstOrDefault(r => string.Equals(r.Name, "Admin", StringComparison.OrdinalIgnoreCase));
        if (adminRole == null)
        {
            adminRole = new Role { Name = "Admin", Description = "Quản trị viên hệ thống" };
            _context.Roles.Add(adminRole);
        }

        var customerRole = existingRoles.FirstOrDefault(r => string.Equals(r.Name, "Customer", StringComparison.OrdinalIgnoreCase));
        if (customerRole == null)
        {
            customerRole = new Role { Name = "Customer", Description = "Khách hàng mua vé" };
            _context.Roles.Add(customerRole);
        }

        var operatorRole = existingRoles.FirstOrDefault(r => string.Equals(r.Name, "Operator", StringComparison.OrdinalIgnoreCase));
        if (operatorRole == null)
        {
            operatorRole = new Role { Name = "Operator", Description = "Đối tác nhà xe" };
            _context.Roles.Add(operatorRole);
        }

        await _context.SaveChangesAsync(ct);

        // 2. Optional local sign-in accounts. Passwords never live in source control: the accounts are
        // created only when the developer supplies a password for this environment.
        await EnsureSandboxUserAsync(
            "admin@tripgo.test", "Quản Trị Viên Sandbox", "0900000091", adminRole.Id,
            Environment.GetEnvironmentVariable("TRIPGO_SANDBOX_ADMIN_PASSWORD") ?? "AdminPassword123!", ct);
        await EnsureSandboxUserAsync(
            "customer@tripgo.test", "Khách Hàng Sandbox", "0900000092", customerRole.Id,
            Environment.GetEnvironmentVariable("TRIPGO_SANDBOX_CUSTOMER_PASSWORD") ?? "CustomerPassword123!", ct);

        await _context.SaveChangesAsync(ct);
    }

    private async Task EnsureSandboxUserAsync(
        string email, string fullName, string phone, int roleId, string? password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var existingUser = await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), ct);
        if (existingUser != null)
        {
            if (string.Equals(email, "admin@tripgo.test", StringComparison.OrdinalIgnoreCase))
            {
                existingUser.PasswordHash = _passwordHasher.HashPassword(password);
            }

            return;
        }

        if (await _context.Users.AnyAsync(u => u.PhoneNumber == phone, ct))
        {
            return;
        }

        _context.Users.Add(new User
        {
            FullName = fullName,
            Email = email,
            PhoneNumber = phone,
            PasswordHash = _passwordHasher.HashPassword(password),
            RoleId = roleId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        _logger.LogInformation("Seeded sandbox account {Email}.", email);
    }

    public async Task RepairPickupDropoffUnicodeAsync(CancellationToken ct = default)
    {
        _context.EnsureWritesTargetIsolatedTestDatabase();
        var dbName = _context.Database.GetDbConnection().Database;

        var points = await _context.PickupDropoffPoints.Where(p => p.Id <= 30).ToListAsync(ct);
        int repairedCount = 0;

        foreach (var p in points)
        {
            if (VerifiedPointsMap.TryGetValue(p.Id, out var verified))
            {
                bool modified = false;
                if (p.Name != verified.Name || p.Address != verified.Address)
                {
                    _logger.LogInformation("Repairing Unicode for Point ID {Id}: '{OldName}' -> '{NewName}'", p.Id, p.Name, verified.Name);
                    p.Name = verified.Name;
                    p.Address = verified.Address;
                    modified = true;
                }

                if (!p.Latitude.HasValue || !p.Longitude.HasValue || p.Latitude.Value != verified.Lat || p.Longitude.Value != verified.Lng)
                {
                    p.Latitude = verified.Lat;
                    p.Longitude = verified.Lng;
                    modified = true;
                }

                if (modified)
                {
                    repairedCount++;
                }
            }
        }

        if (repairedCount > 0)
        {
            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("Successfully repaired Unicode encoding for {Count} PickupDropoffPoints in {Database}.", repairedCount, dbName);
        }
    }

    private static readonly Dictionary<long, (string Name, string Address, decimal Lat, decimal Lng)> VerifiedPointsMap = new()
    {
        [1] = ("Bến xe Miền Tây", "395 Kinh Dương Vương, Phường An Lạc, Quận Bình Tân, TP.HCM", 10.7497m, 106.6186m),
        [2] = ("Văn phòng Quận 5", "147 Lê Hồng Phong, Phường 3, Quận 5, TP.HCM", 10.7588m, 106.6775m),
        [3] = ("Trạm Hàng Xanh", "486E Điện Biên Phủ, Phường 21, Quận Bình Thạnh, TP.HCM", 10.7997m, 106.7118m),
        [4] = ("Trạm Đức Trọng (Phi Nôm)", "Thôn Quảng Hiệp, Xã Hiệp Thạnh, Huyện Đức Trọng, Lâm Đồng", 11.7378m, 108.3750m),
        [5] = ("Bưu điện TP. Đà Lạt", "02 Trần Phú, Phường 3, TP. Đà Lạt, Lâm Đồng", 11.9392m, 108.4372m),
        [6] = ("Bến xe Đa Thiện", "01 Mai Anh Đào, Phường 8, TP. Đà Lạt, Lâm Đồng", 11.9689m, 108.4485m),
        [7] = ("Bến xe Miền Tây (FUTA)", "395 Kinh Dương Vương, Phường An Lạc, Quận Bình Tân, TP.HCM", 10.7497m, 106.6186m),
        [8] = ("Văn phòng Lê Hồng Phong (FUTA)", "231-233 Lê Hồng Phong, Phường 4, Quận 5, TP.HCM", 10.7592m, 106.6780m),
        [9] = ("Bến xe Miền Đông Mới (FUTA)", "501 Hoàng Hữu Nam, Phường Long Bình, TP. Thủ Đức, TP.HCM", 10.8753m, 106.8164m),
        [10] = ("Bến xe Liên tỉnh Đà Lạt (FUTA)", "01 Tô Hiến Thành, Phường 3, TP. Đà Lạt, Lâm Đồng", 11.9298m, 108.4447m),
        [11] = ("Văn phòng Trần Hưng Đạo (FUTA)", "22 Trần Hưng Đạo, Phường 10, TP. Đà Lạt, Lâm Đồng", 11.9442m, 108.4556m),
        [12] = ("Bến xe Phía Nam Nha Trang (FUTA)", "Đường 23/10, Xã Vĩnh Trung, TP. Nha Trang, Khánh Hòa", 12.2472m, 109.1558m),
        [13] = ("Văn phòng Cam Ranh", "08 Tố Hữu, Phường Cam Thuần, TP. Cam Ranh, Khánh Hòa", 11.9214m, 109.1591m),
        [14] = ("Văn phòng CT3 VCN Phước Hải", "Chung cư CT3 Tố Hữu, KĐT VCN Phước Hải, TP. Nha Trang, Khánh Hòa", 12.2415m, 109.1834m),
        [15] = ("Văn phòng Hoàng Hoa Thám", "12D Hoàng Hoa Thám, Phường Lộc Thọ, TP. Nha Trang, Khánh Hòa", 12.2442m, 109.1945m),
        [16] = ("Văn phòng Phạm Ngũ Lão", "313 Phạm Ngũ Lão, Phường Phạm Ngũ Lão, Quận 1, TP.HCM", 10.7688m, 106.6917m),
        [17] = ("Văn phòng Trần Phú", "295 Trần Phú, Phường 8, Quận 5, TP.HCM", 10.7554m, 106.6702m),
        [18] = ("Trạm Đón Hàng Xanh", "Điện Biên Phủ, Phường 21, Quận Bình Thạnh, TP.HCM", 10.7997m, 106.7118m),
        [19] = ("Văn phòng Giải Phóng", "789 Giải Phóng, Phường Giáp Bát, Quận Hoàng Mai, Hà Nội", 20.9808m, 105.8415m),
        [20] = ("Văn phòng Trần Nhật Duật", "114 Trần Nhật Duật, Phường Hàng Buồm, Quận Hoàn Kiếm, Hà Nội", 21.0345m, 105.8546m),
        [21] = ("Văn phòng Cầu Giấy", "07 Phạm Văn Đồng, Phường Mai Dịch, Quận Cầu Giấy, Hà Nội", 21.0368m, 105.7825m),
        [22] = ("Trạm Ngã tư Sân bay Nội Bài", "Trạm thu phí Võ Nguyên Giáp, Sóc Sơn, Hà Nội", 21.2185m, 105.7958m),
        [23] = ("Bến xe Trung tâm Lào Cai", "Tổ 19, Phường Bình Minh, TP. Lào Cai, Lào Cai", 22.4856m, 103.9707m),
        [24] = ("Bến xe Sa Pa", "Đường Điện Biên Phủ, Thị xã Sa Pa, Lào Cai", 22.3364m, 103.8438m),
        [25] = ("Văn phòng 571 Điện Biên Phủ", "571 Điện Biên Phủ, Thị xã Sa Pa, Lào Cai", 22.3385m, 103.8465m),
        [26] = ("Văn phòng Celadon City Tân Phú", "E1.30 Celadon City, 36 Bờ Bao Tân Thắng, Tân Phú, TP.HCM", 10.8012m, 106.6165m),
        [27] = ("Bến xe Miền Đông Cũ", "292 Đinh Bộ Lĩnh, Phường 26, Quận Bình Thạnh, TP.HCM", 10.8142m, 106.7114m),
        [28] = ("Trạm Ngã 4 Bình Phước", "Quốc lộ 13, Phường Hiệp Bình Phước, TP. Thủ Đức, TP.HCM", 10.8525m, 106.7145m),
        [29] = ("Bến xe Phú Tài", "Ngã 3 Phú Tài, Phường Trần Quang Diệu, TP. Quy Nhơn, Bình Định", 13.7712m, 109.1654m),
        [30] = ("Bến xe Trung tâm Quy Nhơn", "71 Tây Sơn, Phường Ghềnh Ráng, TP. Quy Nhơn, Bình Định", 13.7554m, 109.2144m)
    };
}
