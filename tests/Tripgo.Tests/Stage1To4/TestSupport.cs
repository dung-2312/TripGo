using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Tripgo.Application.DTOs;
using Tripgo.Application.Interfaces;
using Tripgo.Application.Services;
using Tripgo.Application.Services.Temporary;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;
using Tripgo.Infrastructure.Persistence;
using Tripgo.Infrastructure.Persistence.Repositories;
using Tripgo.Infrastructure.Services;

namespace Tripgo.Tests.Stage1To4;

internal sealed class FixedSession : IBookingSessionContext
{
    public FixedSession(string? owner, long? userId = null)
    {
        Owner = owner;
        UserId = userId;
    }

    public string? Owner { get; }
    public long? UserId { get; }
    public Task<string?> GetOwnerIdentifierAsync() => Task.FromResult(Owner);
    public Task<long?> GetUserIdAsync() => Task.FromResult(UserId);
}

internal sealed class PlainTokenProtector : ITicketTokenProtector
{
    public string Protect(string token) => "enc:" + token;
    public string? Unprotect(string protectedToken) => protectedToken.StartsWith("enc:", StringComparison.Ordinal) ? protectedToken[4..] : null;
}

/// <summary>
/// Everything a Stage 1-4 test creates carries one run marker, and is removed afterwards from the
/// isolated database in dependency order.
/// </summary>
internal sealed class Stage
{
    public string Marker { get; } = Guid.NewGuid().ToString("N")[..10];
    public string Owner { get; } = Guid.NewGuid().ToString("N");

    public static AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(TestDatabase.ConnectionString, sql => sql.EnableRetryOnFailure().CommandTimeout(120))
            .Options;
        var context = new AppDbContext(options);
        context.EnsureWritesTargetIsolatedTestDatabase();
        return context;
    }

    public static IConfiguration SandboxConfig { get; } = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Payments:Sandbox:CallbackSecret"] = "test-only-callback-secret" })
        .Build();

    public static IConfiguration WorkerConfig { get; } = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Workers:BookingExpiration:Enabled"] = "true",
            ["Workers:BookingExpiration:TargetDatabase"] = "TripgoDb_IsolatedTest",
            ["Workers:BookingConfirmationEmail:Enabled"] = "true",
            ["Workers:BookingConfirmationEmail:TargetDatabase"] = "TripgoDb_IsolatedTest"
        })
        .Build();

    public static SandboxPaymentProvider Gateway { get; } = new(SandboxConfig);

    public BookingService Bookings(AppDbContext context, string? owner = null, long? userId = null) =>
        new(new BookingRepository(context), new FixedSession(owner ?? Owner, userId));

    public PaymentService Payments(AppDbContext context, string? owner = null) =>
        new(new BookingRepository(context), new FixedSession(owner ?? Owner));

    public PaymentGatewayService PaymentGateway(
        AppDbContext context,
        string? owner = null,
        long? userId = null,
        IEnumerable<IPaymentProvider>? providers = null)
    {
        var session = new FixedSession(owner ?? Owner, userId);
        var repository = new AdminRepository(context);
        return new PaymentGatewayService(
            new PaymentGatewayRepository(context),
            session,
            new AdminAccessGuard(session, repository),
            providers ?? new[] { Gateway });
    }

    public AdminService Admin(AppDbContext context, long? userId)
    {
        var session = new FixedSession(Owner, userId);
        var repository = new AdminRepository(context);
        var guard = new AdminAccessGuard(session, repository);
        var bookings = new BookingService(new BookingRepository(context), session);
        var trips = new TripService(new TripRepository(context), new InMemoryTemporaryBookingStore(), bookings);
        var payments = new PaymentGatewayService(new PaymentGatewayRepository(context), session, guard, new[] { Gateway });
        return new AdminService(guard, repository, trips, payments);
    }

    public TicketService Tickets(AppDbContext context, string? owner = null, long? userId = null)
    {
        var session = new FixedSession(owner ?? Owner, userId);
        return new TicketService(new TicketRepository(context), session, new AdminAccessGuard(session, new AdminRepository(context)), new PlainTokenProtector());
    }

    public async Task<long> CreateUserAsync(AppDbContext context, string role, bool isActive = true, long? operatorId = null)
    {
        var roleId = await context.Roles.Where(r => r.Name == role).Select(r => r.Id).FirstAsync();
        var user = new User
        {
            FullName = "Stage Test",
            Email = $"stage-{Marker}-{Guid.NewGuid():N}@tripgo.test",
            PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}",
            PasswordHash = "not-a-real-hash",
            RoleId = roleId,
            IsActive = isActive,
            OperatorId = operatorId
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return user.Id;
    }

    public async Task<Trip> CreateTripAsync(AppDbContext context, bool sandbox = true, DateTimeOffset? departure = null)
    {
        var route = await context.Routes.AsNoTracking().FirstAsync(r => r.IsActive);
        var busId = await context.Buses.Where(b => b.IsActive && b.SeatLayout.Items.Any()).Select(b => b.Id).FirstAsync();
        var when = departure ?? DateTimeOffset.UtcNow.AddDays(30).AddMinutes(Random.Shared.Next(1, 100000));
        var trip = new Trip
        {
            TripCode = $"ST-{Marker}-{Guid.NewGuid():N}"[..28],
            RouteId = route.Id,
            BusId = busId,
            DepartureTime = when,
            ExpectedArrivalTime = when.AddHours(6),
            BasePrice = 300_000m,
            Status = TripStatus.Scheduled,
            IsDemoTrip = sandbox,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.Trips.Add(trip);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return trip;
    }

    /// <summary>Holds real seats and creates a real pending booking through the public services.</summary>
    public async Task<(Trip Trip, BookingDetailDto Booking, List<TripSeatDto> Seats)> CreatePendingBookingAsync(AppDbContext context, string? owner = null, int seats = 1, long? userId = null)
    {
        var trip = await CreateTripAsync(context);
        var bookings = Bookings(context, owner, userId);
        await bookings.EnsureDemoTripInventoryAsync(trip.Id);
        var all = await bookings.GetSeatsForTripAsync(trip.Id);
        var codes = all.Take(seats).Select(s => s.SeatCode).ToList();

        var hold = await bookings.HoldSeatsAsync(new SeatHoldRequestDto { TripId = trip.Id, SeatCodes = codes, HoldMinutes = 10 });
        Assert.True(hold.Success, hold.Message);

        var created = await bookings.CreateBookingAsync(new CreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = hold.HoldToken!,
            SeatCodes = codes,
            PassengerName = "Nguyen Van Khach",
            PassengerPhone = "0912345678",
            PassengerEmail = $"stage.{Marker}@tripgo.test",
            ContactEmailConfirmed = true
        });
        Assert.True(created.Success, created.Message);

        var detail = await bookings.GetBookingByCodeAsync(created.BookingCode!);
        return (trip, detail!, all);
    }

    public async Task CleanupAsync()
    {
        await using var context = NewContext();
        var tripIds = await context.Trips.Where(t => t.TripCode.Contains(Marker)).Select(t => t.Id).ToListAsync();
        var bookingIds = await context.Bookings.Where(b => tripIds.Contains(b.TripId)).Select(b => b.Id).ToListAsync();
        var codes = await context.PaymentTransactions.Where(p => bookingIds.Contains(p.BookingId)).Select(p => p.TransactionCode).ToListAsync();
        var seatIds = await context.TripSeats.Where(s => tripIds.Contains(s.TripId)).Select(s => s.Id).ToListAsync();
        var credentialIds = await context.TicketCredentials.Where(c => bookingIds.Contains(c.BookingId)).Select(c => c.Id).ToListAsync();

        await context.TicketVerificationAttempts.Where(a => a.TicketCredentialId != null && credentialIds.Contains(a.TicketCredentialId.Value)).ExecuteDeleteAsync();
        await context.TicketCredentials.Where(c => credentialIds.Contains(c.Id)).ExecuteDeleteAsync();
        await context.PaymentWebhookEvents.Where(e => e.TransactionCode != null && codes.Contains(e.TransactionCode)).ExecuteDeleteAsync();
        await context.BookingConfirmationEmailOutbox.Where(e => bookingIds.Contains(e.BookingId)).ExecuteDeleteAsync();
        await context.BookingPassengers.Where(p => bookingIds.Contains(p.BookingId)).ExecuteDeleteAsync();
        await context.BookingSeats.Where(s => bookingIds.Contains(s.BookingId)).ExecuteDeleteAsync();
        await context.PaymentTransactions.Where(p => bookingIds.Contains(p.BookingId)).ExecuteDeleteAsync();
        await context.SeatHolds.Where(h => seatIds.Contains(h.TripSeatId)).ExecuteDeleteAsync();
        await context.Bookings.Where(b => bookingIds.Contains(b.Id)).ExecuteDeleteAsync();
        await context.TripSeats.Where(s => seatIds.Contains(s.Id)).ExecuteDeleteAsync();
        await context.TripDrivers.Where(d => tripIds.Contains(d.TripId)).ExecuteDeleteAsync();
        await context.TripStops.Where(s => tripIds.Contains(s.TripId)).ExecuteDeleteAsync();
        await context.Trips.Where(t => tripIds.Contains(t.Id)).ExecuteDeleteAsync();
        await context.Drivers.Where(d => d.LicenseNumber.Contains(Marker)).ExecuteDeleteAsync();
        await context.Users.Where(u => u.Email.Contains(Marker)).ExecuteDeleteAsync();
        await context.AdminAuditLogs.Where(a => a.ActorEmail.Contains(Marker)).ExecuteDeleteAsync();

        var buses = await context.Buses.Where(b => b.LicensePlate.Contains(Marker)).Select(b => new { b.Id, b.SeatLayoutId }).ToListAsync();
        await context.Buses.Where(b => b.LicensePlate.Contains(Marker)).ExecuteDeleteAsync();
        var layoutIds = buses.Select(b => b.SeatLayoutId).ToList();
        await context.SeatLayoutItems.Where(i => layoutIds.Contains(i.SeatLayoutId)).ExecuteDeleteAsync();
        await context.SeatLayouts.Where(l => layoutIds.Contains(l.Id)).ExecuteDeleteAsync();

        var routeIds = await context.Routes.Where(r => r.Code.Contains(Marker)).Select(r => r.Id).ToListAsync();
        await context.RouteStops.Where(s => routeIds.Contains(s.RouteId)).ExecuteDeleteAsync();
        await context.Routes.Where(r => routeIds.Contains(r.Id)).ExecuteDeleteAsync();
        await context.PickupDropoffPoints.Where(p => p.Name.Contains(Marker)).ExecuteDeleteAsync();
        await context.Operators.Where(o => o.Code.Contains(Marker)).ExecuteDeleteAsync();
    }
}

internal sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "Tripgo.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
