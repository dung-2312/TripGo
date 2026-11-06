using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tripgo.Application.Interfaces;
using Tripgo.Application.Interfaces.Persistence;
using Tripgo.Application.DTOs;
using Tripgo.Application.Services;
using Tripgo.Application.Services.Temporary;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;
using Tripgo.Infrastructure.Persistence;
using Tripgo.Infrastructure.Persistence.Repositories;
using Tripgo.Web.Services;
using Xunit;

namespace Tripgo.Tests;

[Collection("Database")]
public class BookingCoreTests : IAsyncLifetime
{
    private static readonly string IsolatedConnectionString = TestDatabase.ConnectionString;
    private readonly string _runMarker = Guid.NewGuid().ToString("N")[..8];
    private readonly string _defaultOwner = Guid.NewGuid().ToString("N");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var context = CreateIsolatedContext();
        var tripIds = await context.Trips
            .Where(trip => trip.TripCode.Contains(_runMarker))
            .Select(trip => trip.Id)
            .ToListAsync();
        if (tripIds.Count == 0) return;

        var bookingIds = await context.Bookings
            .Where(booking => tripIds.Contains(booking.TripId))
            .Select(booking => booking.Id)
            .ToListAsync();
        var seatIds = await context.TripSeats
            .Where(seat => tripIds.Contains(seat.TripId))
            .Select(seat => seat.Id)
            .ToListAsync();

        await context.BookingConfirmationEmailOutbox
            .Where(item => bookingIds.Contains(item.BookingId))
            .ExecuteDeleteAsync();
        await context.BookingPassengers.Where(passenger => bookingIds.Contains(passenger.BookingId)).ExecuteDeleteAsync();
        await context.BookingSeats.Where(seat => bookingIds.Contains(seat.BookingId)).ExecuteDeleteAsync();
        await context.PaymentTransactions.Where(p => bookingIds.Contains(p.BookingId)).ExecuteDeleteAsync();
        await context.SeatHolds.Where(hold => seatIds.Contains(hold.TripSeatId)).ExecuteDeleteAsync();
        await context.Bookings.Where(booking => bookingIds.Contains(booking.Id)).ExecuteDeleteAsync();
        await context.TripSeats.Where(seat => seatIds.Contains(seat.Id)).ExecuteDeleteAsync();
        await context.Trips.Where(trip => tripIds.Contains(trip.Id)).ExecuteDeleteAsync();
    }

    private AppDbContext CreateIsolatedContext()
    {
        var target = new SqlConnectionStringBuilder(IsolatedConnectionString);
        if (!string.Equals(target.InitialCatalog, "TripgoDb_IsolatedTest", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Booking integration tests may only target TripgoDb_IsolatedTest.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(IsolatedConnectionString, sqlServer => sqlServer.EnableRetryOnFailure().CommandTimeout(120))
            .Options;
        return new AppDbContext(options);
    }

    private BookingService CreateService(IBookingRepository repository, string? ownerIdentifier = null) =>
        new(repository, new FixedBookingSessionContext(ownerIdentifier ?? _defaultOwner));

    private BookingService CreateServiceWithoutSession(IBookingRepository repository) =>
        new(repository, new FixedBookingSessionContext(null));

    private async Task<Trip> CreateTestTripAsync(AppDbContext context, bool isDemo, string codePrefix = "TEST")
    {
        var op = await context.Operators.FirstAsync(o => o.IsActive);
        var route = await context.Routes.Include(r => r.Stops).FirstAsync(r => r.IsActive);
        var bus = await context.Buses.Include(b => b.SeatLayout).ThenInclude(l => l.Items).FirstAsync(b => b.IsActive && b.SeatLayout != null && b.SeatLayout.Items.Any());

        var trip = new Trip
        {
            TripCode = $"{codePrefix}-{_runMarker}-{Guid.NewGuid():N}"[..20],
            RouteId = route.Id,
            BusId = bus.Id,
            DepartureTime = DateTimeOffset.UtcNow.AddDays(2),
            ExpectedArrivalTime = DateTimeOffset.UtcNow.AddDays(2).AddHours(8),
            BasePrice = 350_000m,
            Status = TripStatus.Scheduled,
            IsDemoTrip = isDemo,
            CreatedAt = DateTimeOffset.UtcNow
        };

        context.Trips.Add(trip);
        await context.SaveChangesAsync();
        return trip;
    }

    private sealed class FixedBookingSessionContext : IBookingSessionContext
    {
        private readonly string? _ownerIdentifier;
        private readonly long? _userId;

        public FixedBookingSessionContext(string? ownerIdentifier, long? userId = null)
        {
            _ownerIdentifier = ownerIdentifier;
            _userId = userId;
        }

        public Task<string?> GetOwnerIdentifierAsync() => Task.FromResult(_ownerIdentifier);
        public Task<long?> GetUserIdAsync() => Task.FromResult(_userId);
    }

    private PaymentService CreatePaymentService(IBookingRepository repository, string? ownerIdentifier = null) =>
        new(repository, new FixedBookingSessionContext(ownerIdentifier ?? _defaultOwner));

    private async Task RunConfirmationEmailWorkerAsync(IEmailSender emailSender)
    {
        var services = new ServiceCollection();
        services.AddScoped<AppDbContext>(_ => CreateIsolatedContext());
        services.AddScoped<IEmailSender>(_ => emailSender);
        await using var provider = services.BuildServiceProvider();
        var worker = new BookingConfirmationEmailWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BookingConfirmationEmailWorker>.Instance,
            Tripgo.Tests.Stage1To4.Stage.WorkerConfig,
            new Tripgo.Tests.Stage1To4.TestHostEnvironment());

        await worker.ProcessBatchAsync(CancellationToken.None);
    }

    private static Task DeferOtherOutboxMessagesAsync(AppDbContext context, long outboxId)
    {
        var deferredUntil = DateTimeOffset.UtcNow.AddDays(1);
        return context.BookingConfirmationEmailOutbox
            .Where(item => item.Id != outboxId &&
                           (item.Status == BookingConfirmationEmailOutbox.Pending ||
                            item.Status == BookingConfirmationEmailOutbox.Processing))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.NextAttemptAt, deferredUntil)
                .SetProperty(item => item.LockedUntil, deferredUntil));
    }

    private sealed class RetryingEmailSender : IEmailSender
    {
        public bool ShouldFail { get; set; } = true;
        public int BookingConfirmationAttempts { get; private set; }

        public Task<bool> SendPasswordResetAsync(
            string email,
            string token,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> SendBookingConfirmationAsync(
            BookingConfirmationEmailDto confirmation,
            CancellationToken cancellationToken = default)
        {
            if (confirmation.MessageType == BookingConfirmationEmailOutbox.TypeBookingConfirmation)
            {
                BookingConfirmationAttempts++;
            }

            if (ShouldFail)
            {
                throw new InvalidOperationException("Simulated SMTP failure.");
            }

            return Task.FromResult(true);
        }
    }

    // -------------------------------------------------------------
    // TEST 1: Two simultaneous users holding the same seat
    // -------------------------------------------------------------
    [Fact]
    public async Task Test01_TwoSimultaneousUsers_HoldingSameSeat_OnlyOneSucceeds()
    {
        long tripId;
        string targetSeatCode;
        await using (var setupContext = CreateIsolatedContext())
        {
            var trip = await CreateTestTripAsync(setupContext, isDemo: true, codePrefix: "CONC");
            var setupService = CreateService(new BookingRepository(setupContext));
            await setupService.EnsureDemoTripInventoryAsync(trip.Id);
            tripId = trip.Id;
            targetSeatCode = (await setupService.GetSeatsForTripAsync(trip.Id)).First().SeatCode;
        }

        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<SeatHoldResultDto> AttemptHoldAsync(string owner)
        {
            await using var operationContext = CreateIsolatedContext();
            var service = CreateService(new BookingRepository(operationContext), owner);
            await start.Task;
            return await service.HoldSeatsAsync(new SeatHoldRequestDto
            {
                TripId = tripId,
                SeatCodes = new List<string> { targetSeatCode },
                ExistingHoldToken = $"HOLD-{Guid.NewGuid():N}",
                HoldMinutes = 10
            });
        }

        var firstAttempt = AttemptHoldAsync(Guid.NewGuid().ToString("N"));
        var secondAttempt = AttemptHoldAsync(Guid.NewGuid().ToString("N"));
        start.SetResult(true);
        var results = await Task.WhenAll(firstAttempt, secondAttempt);

        Assert.Single(results, result => result.Success);
        Assert.Single(results, result => !result.Success);
    }

    // -------------------------------------------------------------
    // TEST 2: Two users attempt to book the same seat concurrently
    // -------------------------------------------------------------
    [Fact]
    public async Task Test02_TwoUsersBookingSameSeatConcurrently_OnlyOneSucceeds()
    {
        long tripId;
        string targetSeatCode;
        string ownerA = Guid.NewGuid().ToString("N");
        string ownerB = Guid.NewGuid().ToString("N");
        string tokenA = $"HOLD-{Guid.NewGuid():N}";
        string tokenB = $"HOLD-{Guid.NewGuid():N}";
        await using (var setupContext = CreateIsolatedContext())
        {
            var trip = await CreateTestTripAsync(setupContext, isDemo: true, codePrefix: "RACE");
            var setupService = CreateService(new BookingRepository(setupContext), ownerA);
            await setupService.EnsureDemoTripInventoryAsync(trip.Id);
            tripId = trip.Id;
            targetSeatCode = (await setupService.GetSeatsForTripAsync(trip.Id)).First().SeatCode;
            var tripSeat = await setupContext.TripSeats.FirstAsync(seat => seat.TripId == tripId && seat.SeatCode == targetSeatCode);
            setupContext.SeatHolds.AddRange(
                NewActiveHold(tripSeat.Id, tokenA, ownerA),
                NewActiveHold(tripSeat.Id, tokenB, ownerB));
            tripSeat.Status = TripSeatStatus.Held;
            await setupContext.SaveChangesAsync();
        }

        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<BookingResultDto> ConfirmAsync(string owner, string token, string passenger, string phone)
        {
            await using var operationContext = CreateIsolatedContext();
            var service = CreateService(new BookingRepository(operationContext), owner);
            await start.Task;
            return await service.CreateBookingAsync(new TestCreateBookingRequestDto
            {
                TripId = tripId,
                HoldToken = token,
                SeatCodes = new List<string> { targetSeatCode },
                PassengerName = passenger,
                PassengerPhone = phone
            });
        }

        var firstAttempt = ConfirmAsync(ownerA, tokenA, "Nguyễn Văn Một", "0912345678");
        var secondAttempt = ConfirmAsync(ownerB, tokenB, "Nguyễn Văn Hai", "0987654321");
        start.SetResult(true);
        var results = await Task.WhenAll(firstAttempt, secondAttempt);

        Assert.Single(results, result => result.Success);
        Assert.Single(results, result => !result.Success);
    }

    // -------------------------------------------------------------
    // TEST 3: Duplicate HoldToken confirmation is serialized
    // -------------------------------------------------------------
    [Fact]
    public async Task Test03_DuplicateHoldTokenConfirmedConcurrently_OnlyOneSucceeds()
    {
        long tripId;
        string seatCode;
        var owner = Guid.NewGuid().ToString("N");
        var token = $"HOLD-{Guid.NewGuid():N}";
        await using (var setupContext = CreateIsolatedContext())
        {
            var trip = await CreateTestTripAsync(setupContext, isDemo: true, codePrefix: "DUPL");
            var service = CreateService(new BookingRepository(setupContext), owner);
            await service.EnsureDemoTripInventoryAsync(trip.Id);
            tripId = trip.Id;
            seatCode = (await service.GetSeatsForTripAsync(trip.Id)).First().SeatCode;
            Assert.True((await service.HoldSeatsAsync(new SeatHoldRequestDto
            {
                TripId = tripId,
                SeatCodes = new List<string> { seatCode },
                ExistingHoldToken = token
            })).Success);
        }

        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<BookingResultDto> ConfirmAsync(string passenger, string phone)
        {
            await using var operationContext = CreateIsolatedContext();
            var service = CreateService(new BookingRepository(operationContext), owner);
            await start.Task;
            return await service.CreateBookingAsync(new TestCreateBookingRequestDto
            {
                TripId = tripId,
                HoldToken = token,
                SeatCodes = new List<string> { seatCode },
                PassengerName = passenger,
                PassengerPhone = phone
            });
        }

        var firstAttempt = ConfirmAsync("Nguyễn Văn Ba", "0901234567");
        var secondAttempt = ConfirmAsync("Nguyễn Văn Bốn", "0901234568");
        start.SetResult(true);
        var results = await Task.WhenAll(firstAttempt, secondAttempt);

        Assert.Single(results, result => result.Success);
        Assert.Single(results, result => !result.Success);
        Assert.Contains(results.Single(result => !result.Success).Message, new[]
        {
            "Mã phiên giữ chỗ này đã được sử dụng để tạo đơn đặt chỗ trước đó.",
            "Phiên giữ chỗ đã được sử dụng hoặc ghế vừa được đặt. Vui lòng chọn lại ghế."
        });
    }

    private static SeatHold NewActiveHold(long tripSeatId, string token, string ownerIdentifier) =>
        new()
        {
            TripSeatId = tripSeatId,
            HoldToken = token,
            OwnerIdentifier = ownerIdentifier,
            HeldAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            Status = SeatHoldStatus.Active
        };

    [Fact]
    public async Task DemoSeatInventoryCreation_RacingInstances_CreatesOneCompleteInventory()
    {
        long tripId;
        int expectedSeatCount;
        await using (var setupContext = CreateIsolatedContext())
        {
            var trip = await CreateTestTripAsync(setupContext, isDemo: true, codePrefix: "INVR");
            tripId = trip.Id;
            expectedSeatCount = (await setupContext.Buses
                .Where(bus => bus.Id == trip.BusId)
                .SelectMany(bus => bus.SeatLayout!.Items)
                .CountAsync(item => item.IsActive));
        }

        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task EnsureInventoryAsync()
        {
            await using var operationContext = CreateIsolatedContext();
            var service = CreateService(new BookingRepository(operationContext));
            await start.Task;
            await service.EnsureDemoTripInventoryAsync(tripId);
        }

        var first = EnsureInventoryAsync();
        var second = EnsureInventoryAsync();
        start.SetResult(true);
        await Task.WhenAll(first, second);

        await using var verifyContext = CreateIsolatedContext();
        var seatCount = await verifyContext.TripSeats.CountAsync(seat => seat.TripId == tripId);
        Assert.Equal(expectedSeatCount, seatCount);
    }

    // -------------------------------------------------------------
    // TEST 3: Hold expiration and reacquisition
    // -------------------------------------------------------------
    [Fact]
    public async Task Test03_HoldExpirationAndReacquisition_AllowsNewHolderAfter10Minutes()
    {
        await using var context = CreateIsolatedContext();
        var repo = new BookingRepository(context);
        var service = CreateService(repo);

        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "EXPR");
        await service.EnsureDemoTripInventoryAsync(trip.Id);
        var seats = await service.GetSeatsForTripAsync(trip.Id);
        var targetSeatCode = seats.First().SeatCode;

        var hold1 = await service.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { targetSeatCode },
            ExistingHoldToken = $"HOLD-{Guid.NewGuid():N}",
            HoldMinutes = 10
        });
        Assert.True(hold1.Success);

        // Manually expire hold in SQL Server
        var seatEntity = await context.TripSeats.FirstAsync(s => s.TripId == trip.Id && s.SeatCode == targetSeatCode);
        var holdEntity = await context.SeatHolds.FirstAsync(h => h.TripSeatId == seatEntity.Id && h.HoldToken == hold1.HoldToken);
        holdEntity.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await context.SaveChangesAsync();

        // Customer 2 reacquires the expired seat
        var newToken = $"HOLD-{Guid.NewGuid():N}";
        var hold2 = await service.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { targetSeatCode },
            ExistingHoldToken = newToken,
            HoldMinutes = 10
        });
        Assert.True(hold2.Success);
        Assert.Equal(newToken, hold2.HoldToken);
    }

    // -------------------------------------------------------------
    // TEST 4: Invalid or stolen HoldToken
    // -------------------------------------------------------------
    [Fact]
    public async Task Test04_InvalidOrStolenHoldToken_Rejected()
    {
        await using var context = CreateIsolatedContext();
        var repo = new BookingRepository(context);
        var service = CreateService(repo);

        var trip1 = await CreateTestTripAsync(context, isDemo: true, codePrefix: "STOL1");
        var trip2 = await CreateTestTripAsync(context, isDemo: true, codePrefix: "STOL2");
        await service.EnsureDemoTripInventoryAsync(trip1.Id);
        await service.EnsureDemoTripInventoryAsync(trip2.Id);

        var seatsTrip1 = await service.GetSeatsForTripAsync(trip1.Id);
        var targetSeatCode = seatsTrip1.First().SeatCode;

        // Hold seat on Trip 1
        var holdTokenTrip1 = $"HOLD-{Guid.NewGuid():N}";
        var hold = await service.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip1.Id,
            SeatCodes = new List<string> { targetSeatCode },
            ExistingHoldToken = holdTokenTrip1,
            HoldMinutes = 10
        });
        Assert.True(hold.Success);

        // Try booking on Trip 2 using HoldToken from Trip 1 (cross-trip stolen token)
        var stolenAttempt = await service.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip2.Id,
            HoldToken = holdTokenTrip1,
            SeatCodes = new List<string> { targetSeatCode },
            PassengerName = "Kẻ Giả Mạo",
            PassengerPhone = "0933333333"
        });
        Assert.False(stolenAttempt.Success);
        Assert.Contains("hết hạn hoặc không hợp lệ", stolenAttempt.Message);

        // Try with arbitrary non-existent token
        var randomTokenAttempt = await service.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip1.Id,
            HoldToken = "HOLD-BOGUS-NON-EXISTENT",
            SeatCodes = new List<string> { targetSeatCode },
            PassengerName = "Kẻ Giả Mạo",
            PassengerPhone = "0933333333"
        });
        Assert.False(randomTokenAttempt.Success);
    }

    // -------------------------------------------------------------
    // TEST 5: Repeated booking request / replay attack
    // -------------------------------------------------------------
    [Fact]
    public async Task Test05_RepeatedBookingRequest_RejectedAsDuplicateReplay()
    {
        await using var context = CreateIsolatedContext();
        var repo = new BookingRepository(context);
        var service = CreateService(repo);

        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "REPL");
        await service.EnsureDemoTripInventoryAsync(trip.Id);
        var seats = await service.GetSeatsForTripAsync(trip.Id);
        var targetSeatCode = seats.First().SeatCode;

        var holdToken = $"HOLD-{Guid.NewGuid():N}";
        var hold = await service.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { targetSeatCode },
            ExistingHoldToken = holdToken,
            HoldMinutes = 10
        });
        Assert.True(hold.Success);

        var bookingRequest = new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = holdToken,
            SeatCodes = new List<string> { targetSeatCode },
            PassengerName = "Lê Thị Lan",
            PassengerPhone = "0911223344"
        };

        var firstBooking = await service.CreateBookingAsync(bookingRequest);
        Assert.True(firstBooking.Success);

        // Exact replay of identical booking request
        var replayBooking = await service.CreateBookingAsync(bookingRequest);
        Assert.False(replayBooking.Success);
        Assert.Contains("đã được sử dụng", replayBooking.Message);
    }

    // -------------------------------------------------------------
    // TEST 6: Transaction rollback on failure leaves no orphan records
    // -------------------------------------------------------------
    [Fact]
    public async Task Test06_TransactionRollbackOnFailure_LeavesNoOrphanRecords()
    {
        await using var context = CreateIsolatedContext();
        var repo = new BookingRepository(context);
        var service = CreateService(repo);

        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "ROLB");
        await service.EnsureDemoTripInventoryAsync(trip.Id);
        var seats = await service.GetSeatsForTripAsync(trip.Id);
        var targetSeatCode = seats.First().SeatCode;

        var initialBookingCount = await context.Bookings.CountAsync();
        var initialBookingSeatCount = await context.BookingSeats.CountAsync();

        // Attempt booking with bogus hold token
        var result = await service.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = "HOLD-INVALID-TOKEN-FAIL",
            SeatCodes = new List<string> { targetSeatCode },
            PassengerName = "Nguyễn Rollback",
            PassengerPhone = "0944556677"
        });
        Assert.False(result.Success);

        // Verify zero orphan rows created
        var postBookingCount = await context.Bookings.CountAsync();
        var postBookingSeatCount = await context.BookingSeats.CountAsync();
        Assert.Equal(initialBookingCount, postBookingCount);
        Assert.Equal(initialBookingSeatCount, postBookingSeatCount);

        var seatEntity = await context.TripSeats.FirstAsync(s => s.TripId == trip.Id && s.SeatCode == targetSeatCode);
        Assert.Equal(TripSeatStatus.Available, seatEntity.Status);
    }

    // -------------------------------------------------------------
    // TEST 7: Duplicate booking-seat ownership prevented by filtered unique index
    // -------------------------------------------------------------
    [Fact]
    public async Task Test07_DuplicateBookingSeatOwnership_PreventedByFilteredUniqueIndex()
    {
        await using var context = CreateIsolatedContext();
        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "UNQ");

        var seat = new TripSeat
        {
            TripId = trip.Id,
            SeatCode = "Z99",
            Floor = 1,
            Row = 1,
            Column = 1,
            Price = 400_000m,
            Status = TripSeatStatus.Booked
        };
        context.TripSeats.Add(seat);
        await context.SaveChangesAsync();

        var booking1 = new Booking
        {
            BookingCode = $"TRIPGO-{Guid.NewGuid():N}"[..12],
            TripId = trip.Id,
            PassengerName = "Hành khách 1",
            PassengerPhone = "0911111111",
            TotalAmount = 400_000m,
            Status = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Paid,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        context.Bookings.Add(booking1);
        await context.SaveChangesAsync();

        var bookingSeat1 = new BookingSeat
        {
            BookingId = booking1.Id,
            TripSeatId = seat.Id,
            SeatCode = "Z99",
            Price = 400_000m,
            IsActive = true
        };
        context.BookingSeats.Add(bookingSeat1);
        await context.SaveChangesAsync();

        // Second booking attempts to directly claim the SAME TripSeatId as active
        var booking2 = new Booking
        {
            BookingCode = $"TRIPGO-{Guid.NewGuid():N}"[..12],
            TripId = trip.Id,
            PassengerName = "Hành khách 2",
            PassengerPhone = "0922222222",
            TotalAmount = 400_000m,
            Status = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Paid,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        context.Bookings.Add(booking2);
        await context.SaveChangesAsync();

        var bookingSeat2 = new BookingSeat
        {
            BookingId = booking2.Id,
            TripSeatId = seat.Id, // Same TripSeatId!
            SeatCode = "Z99",
            Price = 400_000m,
            IsActive = true
        };
        context.BookingSeats.Add(bookingSeat2);

        // SQL Server must throw unique constraint exception on IX_BookingSeats_TripSeatId WHERE [IsActive] = 1
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    // -------------------------------------------------------------
    // TEST 8: Server-authoritative pricing
    // -------------------------------------------------------------
    [Fact]
    public async Task Test08_ServerAuthoritativePricing_CalculatesExclusivelyFromDatabase()
    {
        await using var context = CreateIsolatedContext();
        var repo = new BookingRepository(context);
        var service = CreateService(repo);

        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "PRIC");
        await service.EnsureDemoTripInventoryAsync(trip.Id);
        var seats = await service.GetSeatsForTripAsync(trip.Id);
        var seatsToBook = seats.Take(2).ToList();
        var seatCodes = seatsToBook.Select(s => s.SeatCode).ToList();

        var expectedTotal = seatsToBook.Sum(s => s.Price); // Server calculation from TripSeats.Price

        var holdToken = $"HOLD-{Guid.NewGuid():N}";
        var hold = await service.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = seatCodes,
            ExistingHoldToken = holdToken,
            HoldMinutes = 10
        });
        Assert.True(hold.Success);

        // Client requests insurance, but system protects customer from unsupported charges (0d fee)
        var bookingResult = await service.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = holdToken,
            SeatCodes = seatCodes,
            PassengerName = "Hoàng Kim",
            PassengerPhone = "0966778899",
            HasInsurance = true
        });

        Assert.True(bookingResult.Success);
        Assert.Equal(expectedTotal, bookingResult.TotalAmount); // Matched exact DB seat prices
    }

    // -------------------------------------------------------------
    // TEST 9: Reference trip booking rejection
    // -------------------------------------------------------------
    [Fact]
    public async Task Test09_ReferenceTripBookingRejection_StrictlyEnforced()
    {
        await using var context = CreateIsolatedContext();
        var repo = new BookingRepository(context);
        var service = CreateService(repo);

        var referenceTrip = await CreateTestTripAsync(context, isDemo: false, codePrefix: "REFN");

        // 1. No seat inventory exposed
        var seats = await service.GetSeatsForTripAsync(referenceTrip.Id);
        Assert.Empty(seats);

        // 2. Hold rejected
        var holdResult = await service.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = referenceTrip.Id,
            SeatCodes = new List<string> { "A01" },
            HoldMinutes = 10
        });
        Assert.False(holdResult.Success);
        Assert.Contains("tham khảo", holdResult.Message);

        // 3. Booking creation rejected
        var bookingResult = await service.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = referenceTrip.Id,
            HoldToken = "HOLD-ANY",
            SeatCodes = new List<string> { "A01" },
            PassengerName = "Nguyễn Khách",
            PassengerPhone = "0988990011"
        });
        Assert.False(bookingResult.Success);
        Assert.Contains("tham khảo", bookingResult.Message);
    }

    // -------------------------------------------------------------
    // TEST 10: Correct demo seat layout generation
    // -------------------------------------------------------------
    [Fact]
    public async Task Test10_CorrectDemoSeatLayoutGeneration_PreservesSeatCodesDecksAndTypes()
    {
        await using var context = CreateIsolatedContext();
        var repo = new BookingRepository(context);
        var service = CreateService(repo);

        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "LAYO");
        await service.EnsureDemoTripInventoryAsync(trip.Id);

        var seats = await service.GetSeatsForTripAsync(trip.Id);
        Assert.NotEmpty(seats);

        // Verify structure matches layout items (floors, rows, cols, codes)
        var bus = await context.Buses.Include(b => b.SeatLayout).ThenInclude(l => l.Items).FirstAsync(b => b.Id == trip.BusId);
        var layoutItems = bus.SeatLayout!.Items.Where(i => i.IsActive).ToList();

        Assert.Equal(layoutItems.Count, seats.Count);
        foreach (var layoutItem in layoutItems)
        {
            var match = seats.FirstOrDefault(s => s.SeatCode == layoutItem.SeatCode);
            Assert.NotNull(match);
            Assert.Equal(layoutItem.Floor, match.Floor);
            Assert.Equal(layoutItem.Row, match.Row);
            Assert.Equal(layoutItem.Column, match.Column);
        }

        // Verify idempotency
        await service.EnsureDemoTripInventoryAsync(trip.Id);
        var seatsSecondRun = await service.GetSeatsForTripAsync(trip.Id);
        Assert.Equal(seats.Count, seatsSecondRun.Count);
    }

    // -------------------------------------------------------------
    // TEST 11: Stolen hold token by another session is strictly rejected
    // -------------------------------------------------------------
    [Fact]
    public async Task Test11_StolenHoldToken_ByAnotherSession_StrictlyRejected()
    {
        await using var context = CreateIsolatedContext();
        var repo = new BookingRepository(context);
        var legitimateSessionId = Guid.NewGuid().ToString("N");
        var attackerSessionId = Guid.NewGuid().ToString("N");
        var service = CreateService(repo, legitimateSessionId);
        var attackerService = CreateService(repo, attackerSessionId);
        var noSessionService = CreateServiceWithoutSession(repo);

        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "STOL");
        await service.EnsureDemoTripInventoryAsync(trip.Id);
        var seats = await service.GetSeatsForTripAsync(trip.Id);
        var targetSeatCode = seats.First().SeatCode;

        // Session A holds the seat
        var holdResult = await service.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { targetSeatCode },
            OwnerIdentifier = attackerSessionId,
            HoldMinutes = 10
        });
        Assert.True(holdResult.Success);
        var holdToken = holdResult.HoldToken!;

        // Attacker Session B attempts to confirm booking with stolen holdToken
        var stolenAttempt = await attackerService.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = holdToken,
            OwnerIdentifier = legitimateSessionId,
            SeatCodes = new List<string> { targetSeatCode },
            PassengerName = "Kẻ Giả Mạo",
            PassengerPhone = "0999888777"
        });
        Assert.False(stolenAttempt.Success);

        var tokenReuseAttempt = await attackerService.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seats.Skip(1).First().SeatCode },
            ExistingHoldToken = holdToken,
            OwnerIdentifier = legitimateSessionId,
            HoldMinutes = 10
        });
        Assert.False(tokenReuseAttempt.Success);

        // Missing server-issued identity is rejected even when the request has a token.
        var anonymousAttempt = await noSessionService.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = holdToken,
            OwnerIdentifier = legitimateSessionId,
            SeatCodes = new List<string> { targetSeatCode },
            PassengerName = "Ẩn Danh",
            PassengerPhone = "0999888777"
        });
        Assert.False(anonymousAttempt.Success);

        // Legitimate Session A confirms booking with matching OwnerIdentifier
        var legitimateBooking = await service.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = holdToken,
            OwnerIdentifier = attackerSessionId,
            SeatCodes = new List<string> { targetSeatCode },
            PassengerName = "Nguyễn Chính Chủ",
            PassengerPhone = "0912345678"
        });
        Assert.True(legitimateBooking.Success);
        Assert.False(string.IsNullOrEmpty(legitimateBooking.BookingCode));
        Assert.NotNull(await service.GetBookingByCodeAsync(legitimateBooking.BookingCode!));
        Assert.Null(await attackerService.GetBookingByCodeAsync(legitimateBooking.BookingCode!));
    }

    // -------------------------------------------------------------
    // TEST 12: Booking cancellation atomically releases seats and allows rebooking
    // -------------------------------------------------------------
    [Fact]
    public async Task Test12_BookingCancellation_AtomicallyReleasesSeats_AndAllowsRebooking()
    {
        await using var context = CreateIsolatedContext();
        var repo = new BookingRepository(context);
        var service = CreateService(repo);

        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "CNCL");
        await service.EnsureDemoTripInventoryAsync(trip.Id);
        var seats = await service.GetSeatsForTripAsync(trip.Id);
        var targetSeatCode = seats.First().SeatCode;

        var session1 = $"SESSION-1-{Guid.NewGuid():N}";
        var hold1 = await service.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { targetSeatCode },
            OwnerIdentifier = session1,
            HoldMinutes = 10
        });
        Assert.True(hold1.Success);

        var booking1Result = await service.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = hold1.HoldToken!,
            OwnerIdentifier = session1,
            SeatCodes = new List<string> { targetSeatCode },
            PassengerName = "Khách Hủy",
            PassengerPhone = "0901234567"
        });
        Assert.True(booking1Result.Success);

        // Verify booking in DB is Booked and seat is Booked
        var booking1 = await context.Bookings
            .Include(b => b.BookingSeats)
            .FirstAsync(b => b.BookingCode == booking1Result.BookingCode);
        var tripSeat = await context.TripSeats.FirstAsync(s => s.TripId == trip.Id && s.SeatCode == targetSeatCode);
        Assert.Equal(TripSeatStatus.Booked, tripSeat.Status);
        Assert.True(booking1.BookingSeats.First().IsActive);

        // Cancel booking
        var cancelSuccess = await service.CancelBookingAsync(booking1.Id, "Khách đổi lịch trình");
        Assert.True(cancelSuccess);
        Assert.True(await service.CancelBookingAsync(booking1.Id, "Lặp lại yêu cầu hủy"));

        // Verify atomic state updates in fresh context
        await using var verifyContext = CreateIsolatedContext();
        var cancelledBooking = await verifyContext.Bookings
            .Include(b => b.BookingSeats)
            .FirstAsync(b => b.Id == booking1.Id);
        var releasedTripSeat = await verifyContext.TripSeats.FirstAsync(s => s.TripId == trip.Id && s.SeatCode == targetSeatCode);
        var releasedHold = await verifyContext.SeatHolds.FirstAsync(h => h.HoldToken == hold1.HoldToken);

        Assert.Equal(BookingStatus.Cancelled, cancelledBooking.Status);
        Assert.NotNull(cancelledBooking.CancelledAt);
        Assert.Equal("Khách đổi lịch trình", cancelledBooking.CancellationReason);
        Assert.False(cancelledBooking.BookingSeats.First().IsActive); // BookingSeat deactivated
        Assert.Equal(TripSeatStatus.Available, releasedTripSeat.Status); // TripSeat released
        Assert.Equal(SeatHoldStatus.Released, releasedHold.Status); // Hold marked Released

        // A new customer session now holds and rebooks the exact same seat
        var session2 = $"SESSION-2-{Guid.NewGuid():N}";
        var hold2 = await service.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { targetSeatCode },
            OwnerIdentifier = session2,
            HoldMinutes = 10
        });
        Assert.True(hold2.Success);

        var booking2Result = await service.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = hold2.HoldToken!,
            OwnerIdentifier = session2,
            SeatCodes = new List<string> { targetSeatCode },
            PassengerName = "Khách Đặt Mới",
            PassengerPhone = "0987654321"
        });
        Assert.True(booking2Result.Success);

        // Verify historical preservation: exactly 2 BookingSeat rows for this TripSeat (1 inactive, 1 active)
        await using var historyContext = CreateIsolatedContext();
        var seatHistory = await historyContext.BookingSeats
            .Where(bs => bs.TripSeatId == releasedTripSeat.Id)
            .ToListAsync();
        Assert.Equal(2, seatHistory.Count);
        Assert.Single(seatHistory, bs => !bs.IsActive); // Cancelled booking seat preserved
        Assert.Single(seatHistory, bs => bs.IsActive);  // New active booking seat
    }

    [Fact]
    public async Task BookingStateTransitions_RejectInvalidCreationAndCompletedCancellation()
    {
        await using var context = CreateIsolatedContext();
        var repository = new BookingRepository(context);
        var service = CreateService(repository);
        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "STAT");
        await service.EnsureDemoTripInventoryAsync(trip.Id);
        var seat = (await service.GetSeatsForTripAsync(trip.Id)).First();
        var hold = await service.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seat.SeatCode }
        });
        Assert.True(hold.Success);

        var invalidCreate = await repository.CreateBookingAsync(
            new Booking
            {
                BookingCode = $"TRIPGO-{Guid.NewGuid():N}",
                TripId = trip.Id,
                Status = BookingStatus.Confirmed,
                PaymentStatus = PaymentStatus.Unpaid,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
            },
            new List<BookingSeat> { new() { TripSeatId = seat.Id, SeatCode = seat.SeatCode, Price = seat.Price } },
            new List<BookingPassenger> { new() { FullName = "Trạng thái lỗi", PhoneNumber = "0912345678", IsPrimaryContact = true } },
            hold.HoldToken!,
            _defaultOwner);
        Assert.False(invalidCreate.Success);

        var validCreate = await service.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = hold.HoldToken!,
            SeatCodes = new List<string> { seat.SeatCode },
            PassengerName = "Đặt chỗ hợp lệ",
            PassengerPhone = "0912345678"
        });
        Assert.True(validCreate.Success);
        var booking = await context.Bookings.SingleAsync(item => item.BookingCode == validCreate.BookingCode);
        booking.Status = BookingStatus.Completed;
        booking.PaymentStatus = PaymentStatus.Paid;
        await context.SaveChangesAsync();

        Assert.False(await service.CancelBookingAsync(booking.Id, "Không hỗ trợ hủy sau khi hoàn tất"));
    }

    // -------------------------------------------------------------
    // TEST 13: Booking expiration atomically releases seats and allows rebooking
    // -------------------------------------------------------------
    [Fact]
    public async Task Test13_BookingExpiration_AtomicallyReleasesSeats_AndAllowsRebooking()
    {
        await using var context = CreateIsolatedContext();
        var repo = new BookingRepository(context);
        var service = CreateService(repo);

        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "EXPB");
        await service.EnsureDemoTripInventoryAsync(trip.Id);
        var seats = await service.GetSeatsForTripAsync(trip.Id);
        var targetSeatCode = seats.First().SeatCode;

        var session1 = $"SESSION-EXPR-1-{Guid.NewGuid():N}";
        var hold1 = await service.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { targetSeatCode },
            OwnerIdentifier = session1,
            HoldMinutes = 10
        });
        Assert.True(hold1.Success);

        var booking1Result = await service.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = hold1.HoldToken!,
            OwnerIdentifier = session1,
            SeatCodes = new List<string> { targetSeatCode },
            PassengerName = "Khách Quá Hạn",
            PassengerPhone = "0933445566"
        });
        Assert.True(booking1Result.Success);

        // Artificially expire the booking in database
        var bookingToExpire = await context.Bookings.FirstAsync(b => b.BookingCode == booking1Result.BookingCode);
        bookingToExpire.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await context.SaveChangesAsync();

        // Run stale booking expiration sweeper
        await repo.ExpireStaleBookingsAsync();

        // Verify atomic state updates
        await using var verifyContext = CreateIsolatedContext();
        var expiredBooking = await verifyContext.Bookings
            .Include(b => b.BookingSeats)
            .FirstAsync(b => b.Id == bookingToExpire.Id);
        var releasedTripSeat = await verifyContext.TripSeats.FirstAsync(s => s.TripId == trip.Id && s.SeatCode == targetSeatCode);
        var expiredHold = await verifyContext.SeatHolds.FirstAsync(h => h.HoldToken == hold1.HoldToken);

        Assert.Equal(BookingStatus.Expired, expiredBooking.Status);
        Assert.False(expiredBooking.BookingSeats.First().IsActive);
        Assert.Equal(TripSeatStatus.Available, releasedTripSeat.Status);
        Assert.Equal(SeatHoldStatus.Expired, expiredHold.Status);

        // Verify old hold token cannot be used to book again
        var oldHoldReuseAttempt = await service.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = hold1.HoldToken!,
            OwnerIdentifier = session1,
            SeatCodes = new List<string> { targetSeatCode },
            PassengerName = "Thử Lại Cũ",
            PassengerPhone = "0933445566"
        });
        Assert.False(oldHoldReuseAttempt.Success);

        // A new customer session holds and rebooks the released seat
        var session2 = $"SESSION-EXPR-2-{Guid.NewGuid():N}";
        var hold2 = await service.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { targetSeatCode },
            OwnerIdentifier = session2,
            HoldMinutes = 10
        });
        Assert.True(hold2.Success);

        var booking2Result = await service.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = hold2.HoldToken!,
            OwnerIdentifier = session2,
            SeatCodes = new List<string> { targetSeatCode },
            PassengerName = "Khách Mới Sau Hết Hạn",
            PassengerPhone = "0977889900"
        });
        Assert.True(booking2Result.Success);

        // Verify historical preservation: exactly 2 BookingSeat rows for this TripSeat (1 inactive, 1 active)
        await using var historyContext = CreateIsolatedContext();
        var seatHistory = await historyContext.BookingSeats
            .Where(bs => bs.TripSeatId == releasedTripSeat.Id)
            .ToListAsync();
        Assert.Equal(2, seatHistory.Count);
        Assert.Single(seatHistory, bs => !bs.IsActive); // Expired booking seat preserved
        Assert.Single(seatHistory, bs => bs.IsActive);  // New active booking seat
    }

    // -------------------------------------------------------------
    // TEST 14: Direct booking ownership survives SeatHold expiration or deletion
    // -------------------------------------------------------------
    [Fact]
    public async Task Test14_DirectBookingOwnership_AllowsAccessAndCancellation_EvenIfSeatHoldsAreDeleted()
    {
        string owner = $"OWNER-{Guid.NewGuid():N}";
        string intruder = $"INTRUDER-{Guid.NewGuid():N}";
        string bookingCode;
        long bookingId;
        long tripId;

        await using (var setupContext = CreateIsolatedContext())
        {
            var trip = await CreateTestTripAsync(setupContext, isDemo: true, codePrefix: "OWNR");
            tripId = trip.Id;
            var repo = new BookingRepository(setupContext);
            var service = CreateService(repo, owner);
            await service.EnsureDemoTripInventoryAsync(trip.Id);
            var seat = (await service.GetSeatsForTripAsync(trip.Id)).First();

            var holdToken = $"HOLD-{Guid.NewGuid():N}";
            var hold = await service.HoldSeatsAsync(new SeatHoldRequestDto
            {
                TripId = trip.Id,
                SeatCodes = new List<string> { seat.SeatCode },
                ExistingHoldToken = holdToken,
                OwnerIdentifier = owner,
                HoldMinutes = 10
            });
            Assert.True(hold.Success);

            var bookResult = await service.CreateBookingAsync(new TestCreateBookingRequestDto
            {
                TripId = trip.Id,
                HoldToken = holdToken,
                OwnerIdentifier = owner,
                SeatCodes = new List<string> { seat.SeatCode },
                PassengerName = "Nguyễn Văn Chủ",
                PassengerPhone = "0912345678"
            });
            Assert.True(bookResult.Success);
            bookingCode = bookResult.BookingCode!;
        }

        // Delete all SeatHolds associated with this trip to simulate expiration and cleanup
        await using (var deleteContext = CreateIsolatedContext())
        {
            var tripSeats = await deleteContext.TripSeats.Where(s => s.TripId == tripId).Select(s => s.Id).ToListAsync();
            await deleteContext.SeatHolds.Where(h => tripSeats.Contains(h.TripSeatId)).ExecuteDeleteAsync();

            var verifyBooking = await deleteContext.Bookings.FirstAsync(b => b.BookingCode == bookingCode);
            Assert.Equal(owner, verifyBooking.OwnerIdentifier);
            bookingId = verifyBooking.Id;
        }

        // Legitimate owner retrieves booking despite absent SeatHold
        await using (var ownerContext = CreateIsolatedContext())
        {
            var repo = new BookingRepository(ownerContext);
            var service = CreateService(repo, owner);

            var booking = await service.GetBookingByCodeAsync(bookingCode);
            Assert.NotNull(booking);
            Assert.Equal(bookingCode, booking.BookingCode);
            Assert.Equal("Nguyễn Văn Chủ", booking.PassengerName);

            var byPhone = await service.GetBookingsByPhoneAsync("0912345678");
            Assert.Contains(byPhone, b => b.BookingCode == bookingCode);
        }

        // Unauthorized session cannot retrieve or cancel
        await using (var intruderContext = CreateIsolatedContext())
        {
            var repo = new BookingRepository(intruderContext);
            var service = CreateService(repo, intruder);

            var booking = await service.GetBookingByCodeAsync(bookingCode);
            Assert.Null(booking);

            var cancelAttempt = await service.CancelBookingAsync(bookingId, "Kẻ lạ muốn hủy");
            Assert.False(cancelAttempt);
        }

        // Legitimate owner can cancel directly
        await using (var cancelContext = CreateIsolatedContext())
        {
            var repo = new BookingRepository(cancelContext);
            var service = CreateService(repo, owner);

            var cancelResult = await service.CancelBookingAsync(bookingId, "Chủ vé hủy trực tiếp");
            Assert.True(cancelResult);

            var cancelledBooking = await cancelContext.Bookings.FirstAsync(b => b.BookingCode == bookingCode);
            Assert.Equal(BookingStatus.Cancelled, cancelledBooking.Status);
        }
    }

    // -------------------------------------------------------------
    // TEST 15: Multi-passenger booking persists detailed passenger records
    // -------------------------------------------------------------
    [Fact]
    public async Task Test15_MultiPassengerBooking_PersistsAllPassengerRecords()
    {
        string owner = $"OWNER-MULTI-{Guid.NewGuid():N}";
        string bookingCode;

        await using (var context = CreateIsolatedContext())
        {
            var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "PASS");
            var repo = new BookingRepository(context);
            var service = CreateService(repo, owner);
            await service.EnsureDemoTripInventoryAsync(trip.Id);
            var seats = (await service.GetSeatsForTripAsync(trip.Id)).Take(2).ToList();
            var seatCodes = seats.Select(s => s.SeatCode).ToList();

            var holdToken = $"HOLD-{Guid.NewGuid():N}";
            var hold = await service.HoldSeatsAsync(new SeatHoldRequestDto
            {
                TripId = trip.Id,
                SeatCodes = seatCodes,
                ExistingHoldToken = holdToken,
                OwnerIdentifier = owner,
                HoldMinutes = 10
            });
            Assert.True(hold.Success);

            var passengers = new List<BookingPassengerDto>
            {
                new()
                {
                    FullName = "Nguyễn Văn Đại Diện",
                    PhoneNumber = "0901112233",
                    Email = "daidien@test.com",
                    IdentityCard = "001200001111",
                    IsPrimaryContact = true,
                    SeatCode = seatCodes[0]
                },
                new()
                {
                    FullName = "Trần Thị Đi Cùng",
                    PhoneNumber = "0904445566",
                    Email = "dicung@test.com",
                    IdentityCard = "001200002222",
                    IsPrimaryContact = false,
                    SeatCode = seatCodes[1]
                }
            };

            var bookResult = await service.CreateBookingAsync(new TestCreateBookingRequestDto
            {
                TripId = trip.Id,
                HoldToken = holdToken,
                OwnerIdentifier = owner,
                SeatCodes = seatCodes,
                PassengerName = "Nguyễn Văn Đại Diện",
                PassengerPhone = "0901112233",
                PassengerEmail = "daidien@test.com",
                Passengers = passengers
            });

            Assert.True(bookResult.Success);
            bookingCode = bookResult.BookingCode!;
        }

        // Verify persistence in DB and retrieval through service
        await using (var verifyContext = CreateIsolatedContext())
        {
            var repo = new BookingRepository(verifyContext);
            var service = CreateService(repo, owner);

            var bookingDetail = await service.GetBookingByCodeAsync(bookingCode);
            Assert.NotNull(bookingDetail);
            Assert.Equal(2, bookingDetail.Passengers.Count);

            var primary = bookingDetail.Passengers.First(p => p.IsPrimaryContact);
            Assert.Equal("Nguyễn Văn Đại Diện", primary.FullName);
            Assert.Equal("0901112233", primary.PhoneNumber);
            Assert.Equal("001200001111", primary.IdentityCard);

            var companion = bookingDetail.Passengers.First(p => !p.IsPrimaryContact);
            Assert.Equal("Trần Thị Đi Cùng", companion.FullName);
            Assert.Equal("0904445566", companion.PhoneNumber);
            Assert.Equal("001200002222", companion.IdentityCard);

            // Directly query BookingPassengers table
            var dbPassengers = await verifyContext.BookingPassengers
                .Where(p => p.Booking.BookingCode == bookingCode)
                .OrderBy(p => p.IsPrimaryContact ? 0 : 1)
                .ToListAsync();
            Assert.Equal(2, dbPassengers.Count);
            Assert.True(dbPassengers[0].IsPrimaryContact);
            Assert.Equal("Nguyễn Văn Đại Diện", dbPassengers[0].FullName);
            Assert.False(dbPassengers[1].IsPrimaryContact);
            Assert.Equal("Trần Thị Đi Cùng", dbPassengers[1].FullName);
        }
    }

    // -------------------------------------------------------------
    // TEST 16: Server-authoritative pricing correctly separates base and surcharge amounts
    // -------------------------------------------------------------
    [Fact]
    public async Task Test16_ServerAuthoritativePricing_WithVipSeatSurcharge_CorrectlySeparatesBaseAndSurcharge()
    {
        string owner = $"OWNER-PRICE-{Guid.NewGuid():N}";
        string bookingCode;
        decimal tripBasePrice = 300_000m;
        decimal vipPrice = 380_000m; // 80,000 surcharge

        await using (var context = CreateIsolatedContext())
        {
            var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "VIP");
            trip.BasePrice = tripBasePrice;
            await context.SaveChangesAsync();

            var repo = new BookingRepository(context);
            var service = CreateService(repo, owner);
            await service.EnsureDemoTripInventoryAsync(trip.Id);

            var seats = await context.TripSeats.Where(s => s.TripId == trip.Id).Take(2).ToListAsync();
            seats[0].Price = tripBasePrice; // Standard seat
            seats[1].Price = vipPrice;      // VIP seat with 80,000 surcharge
            await context.SaveChangesAsync();

            var seatCodes = seats.Select(s => s.SeatCode).ToList();
            var holdToken = $"HOLD-{Guid.NewGuid():N}";
            var hold = await service.HoldSeatsAsync(new SeatHoldRequestDto
            {
                TripId = trip.Id,
                SeatCodes = seatCodes,
                ExistingHoldToken = holdToken,
                OwnerIdentifier = owner,
                HoldMinutes = 10
            });
            Assert.True(hold.Success);

            var bookResult = await service.CreateBookingAsync(new TestCreateBookingRequestDto
            {
                TripId = trip.Id,
                HoldToken = holdToken,
                OwnerIdentifier = owner,
                SeatCodes = seatCodes,
                PassengerName = "Nguyễn VIP",
                PassengerPhone = "0988112233",
                HasInsurance = false
            });

            Assert.True(bookResult.Success);
            bookingCode = bookResult.BookingCode!;
            Assert.Equal(680_000m, bookResult.TotalAmount); // 300,000 + 380,000
        }

        await using (var verifyContext = CreateIsolatedContext())
        {
            var booking = await verifyContext.Bookings.FirstAsync(b => b.BookingCode == bookingCode);
            Assert.Equal(600_000m, booking.BaseAmount);      // 2 * 300,000
            Assert.Equal(80_000m, booking.SurchargeAmount);  // VIP surcharge
            Assert.Equal(0m, booking.InsuranceAmount);       // No unsupported charges
            Assert.Equal(680_000m, booking.TotalAmount);     // Server authoritative sum
        }
    }

    // -------------------------------------------------------------
    // TEST 17: Booking DTO validation rejects invalid contact and passenger input
    // -------------------------------------------------------------
    [Fact]
    public async Task Test17_BookingDtoValidation_RejectsInvalidInput()
    {
        string owner = $"OWNER-VAL-{Guid.NewGuid():N}";

        await using var context = CreateIsolatedContext();
        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "VAL");
        var repo = new BookingRepository(context);
        var service = CreateService(repo, owner);
        await service.EnsureDemoTripInventoryAsync(trip.Id);
        var seats = (await service.GetSeatsForTripAsync(trip.Id)).Take(2).ToList();
        var seatCodes = seats.Select(s => s.SeatCode).ToList();

        var holdToken = $"HOLD-{Guid.NewGuid():N}";
        var hold = await service.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = seatCodes,
            ExistingHoldToken = holdToken,
            OwnerIdentifier = owner,
            HoldMinutes = 10
        });
        Assert.True(hold.Success);

        // Case 1: Empty passenger name
        var emptyNameResult = await service.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = holdToken,
            SeatCodes = seatCodes,
            PassengerName = "",
            PassengerPhone = "0912345678"
        });
        Assert.False(emptyNameResult.Success);
        Assert.Contains("họ tên", emptyNameResult.Message, StringComparison.OrdinalIgnoreCase);

        // Case 2: Invalid phone
        var invalidPhoneResult = await service.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = holdToken,
            SeatCodes = seatCodes,
            PassengerName = "Nguyễn Văn Test",
            PassengerPhone = "123"
        });
        Assert.False(invalidPhoneResult.Success);
        Assert.Contains("số điện thoại", invalidPhoneResult.Message, StringComparison.OrdinalIgnoreCase);

        // Case 3: Mismatched passenger count in multi-passenger DTO
        var mismatchedPassengerResult = await service.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = holdToken,
            SeatCodes = seatCodes, // 2 seats
            PassengerName = "Nguyễn Văn Test",
            PassengerPhone = "0912345678",
            Passengers = new List<BookingPassengerDto>
            {
                new() { FullName = "Chỉ Một Người", PhoneNumber = "0912345678" } // Only 1 passenger for 2 seats
            }
        });
        Assert.False(mismatchedPassengerResult.Success);
        Assert.Contains("số lượng", mismatchedPassengerResult.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------
    // TEST 18: Expiration routine safely expires unpaid bookings and preserves confirmed ones
    // -------------------------------------------------------------
    [Fact]
    public async Task Test18_ExpirationRoutine_SafelyExpiresUnpaidBookings_AndLeavesPaidBookingsUntouched()
    {
        string owner = $"OWNER-EXP-{Guid.NewGuid():N}";
        long tripId;
        string expiredBookingCode;
        string confirmedBookingCode;

        await using (var context = CreateIsolatedContext())
        {
            var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "EXPR");
            tripId = trip.Id;
            var repo = new BookingRepository(context);
            var service = CreateService(repo, owner);
            await service.EnsureDemoTripInventoryAsync(tripId);
            var seats = (await service.GetSeatsForTripAsync(tripId)).Take(2).ToList();

            // Booking 1: Will be set to past ExpiresAt (should expire)
            var holdToken1 = $"HOLD-{Guid.NewGuid():N}";
            await service.HoldSeatsAsync(new SeatHoldRequestDto
            {
                TripId = tripId,
                SeatCodes = new List<string> { seats[0].SeatCode },
                ExistingHoldToken = holdToken1,
                OwnerIdentifier = owner,
                HoldMinutes = 10
            });
            var b1Result = await service.CreateBookingAsync(new TestCreateBookingRequestDto
            {
                TripId = tripId,
                HoldToken = holdToken1,
                OwnerIdentifier = owner,
                SeatCodes = new List<string> { seats[0].SeatCode },
                PassengerName = "Khách Hết Hạn",
                PassengerPhone = "0911223344"
            });
            Assert.True(b1Result.Success);
            expiredBookingCode = b1Result.BookingCode!;

            // Booking 2: Will be marked Confirmed and Paid (should NEVER expire)
            var holdToken2 = $"HOLD-{Guid.NewGuid():N}";
            await service.HoldSeatsAsync(new SeatHoldRequestDto
            {
                TripId = tripId,
                SeatCodes = new List<string> { seats[1].SeatCode },
                ExistingHoldToken = holdToken2,
                OwnerIdentifier = owner,
                HoldMinutes = 10
            });
            var b2Result = await service.CreateBookingAsync(new TestCreateBookingRequestDto
            {
                TripId = tripId,
                HoldToken = holdToken2,
                OwnerIdentifier = owner,
                SeatCodes = new List<string> { seats[1].SeatCode },
                PassengerName = "Khách Đã Thanh Toán",
                PassengerPhone = "0988776655"
            });
            Assert.True(b2Result.Success);
            confirmedBookingCode = b2Result.BookingCode!;

            // Adjust in DB: set Booking 1 to expired in past, set Booking 2 to Confirmed/Paid
            var dbB1 = await context.Bookings.FirstAsync(b => b.BookingCode == expiredBookingCode);
            dbB1.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);

            var dbB2 = await context.Bookings.FirstAsync(b => b.BookingCode == confirmedBookingCode);
            dbB2.Status = BookingStatus.Confirmed;
            dbB2.PaymentStatus = PaymentStatus.Paid;
            dbB2.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);

            await context.SaveChangesAsync();
        }

        // Run expiration routine
        await using (var expireContext = CreateIsolatedContext())
        {
            var repo = new BookingRepository(expireContext);
            await repo.ExpireStaleBookingsAsync();
        }

        // Verify state
        await using (var verifyContext = CreateIsolatedContext())
        {
            var b1 = await verifyContext.Bookings
                .Include(b => b.BookingSeats)
                .FirstAsync(b => b.BookingCode == expiredBookingCode);
            Assert.Equal(BookingStatus.Expired, b1.Status);
            Assert.False(b1.BookingSeats.First().IsActive);

            var b2 = await verifyContext.Bookings
                .Include(b => b.BookingSeats)
                .FirstAsync(b => b.BookingCode == confirmedBookingCode);
            Assert.Equal(BookingStatus.Confirmed, b2.Status);
            Assert.Equal(PaymentStatus.Paid, b2.PaymentStatus);
            Assert.True(b2.BookingSeats.First().IsActive);
        }
    }

    // -------------------------------------------------------------
    // TEST 19: TripService multi-passenger booking integration
    // -------------------------------------------------------------
    [Fact]
    public async Task Test19_TripService_MultiPassengerBookingFlow_EndToEndIntegration()
    {
        string owner = $"OWNER-SVC-{Guid.NewGuid():N}";
        long tripId;

        await using var context = CreateIsolatedContext();
        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "SVC");
        tripId = trip.Id;
        var tripRepo = new TripRepository(context);
        var bookingRepo = new BookingRepository(context);
        var bookingService = CreateService(bookingRepo, owner);
        await bookingService.EnsureDemoTripInventoryAsync(tripId);
        var store = new InMemoryTemporaryBookingStore();
        var tripService = new TripService(tripRepo, store, bookingService);

        var seats = (await bookingService.GetSeatsForTripAsync(tripId)).Take(2).ToList();
        var seatCodes = seats.Select(s => s.SeatCode).ToList();

        var customerInfo = new CustomerBookingInfoDto
        {
            Name = "Lê Người Lớn",
            Phone = "0933557799",
            Email = "nguoilon@test.com",
            ContactEmailConfirmed = true,
            OwnerIdentifier = owner,
            HasInsurance = false,
            Passengers = new List<BookingPassengerDto>
            {
                new()
                {
                    FullName = "Lê Người Lớn",
                    PhoneNumber = "0933557799",
                    Email = "nguoilon@test.com",
                    IsPrimaryContact = true,
                    SeatCode = seatCodes[0]
                },
                new()
                {
                    FullName = "Lê Bé Nhỏ",
                    PhoneNumber = "0933557788",
                    IsPrimaryContact = false,
                    SeatCode = seatCodes[1]
                }
            }
        };

        var bookSuccess = await tripService.BookSeatsAsync(tripId, seatCodes, customerInfo);
        Assert.True(bookSuccess);
        Assert.False(string.IsNullOrEmpty(customerInfo.BookingCode));

        // Retrieve through TripService
        var booking = await tripService.GetBookingByCodeAsync(customerInfo.BookingCode);
        Assert.NotNull(booking);
        Assert.Equal(2, booking.Passengers.Count);
        Assert.Contains(booking.Passengers, p => p.FullName == "Lê Người Lớn" && p.IsPrimaryContact);
        Assert.Contains(booking.Passengers, p => p.FullName == "Lê Bé Nhỏ" && !p.IsPrimaryContact);

        var byPhone = await tripService.GetBookingsByPhoneAsync("0933557799");
        Assert.Single(byPhone, b => b.BookingCode == customerInfo.BookingCode);
    }

    // -------------------------------------------------------------
    // TEST 20: Passenger Validation Edge Cases
    // -------------------------------------------------------------
    [Fact]
    public async Task Test20_PassengerValidation_DuplicateSeatAssignmentAndEmailFormat_Rejections()
    {
        string owner = $"OWNER-VAL2-{Guid.NewGuid():N}";
        await using var context = CreateIsolatedContext();
        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "VAL2");
        var repo = new BookingRepository(context);
        var service = CreateService(repo, owner);
        await service.EnsureDemoTripInventoryAsync(trip.Id);
        var seats = (await service.GetSeatsForTripAsync(trip.Id)).Take(2).ToList();
        var seatCodes = seats.Select(s => s.SeatCode).ToList();

        var holdToken = $"HOLD-{Guid.NewGuid():N}";
        var hold = await service.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = seatCodes,
            ExistingHoldToken = holdToken,
            OwnerIdentifier = owner,
            HoldMinutes = 10
        });
        Assert.True(hold.Success);

        // Case 1: Duplicate seat codes in request
        var duplicateSeatCodes = await service.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = holdToken,
            SeatCodes = new List<string> { seatCodes[0], seatCodes[0] },
            PassengerName = "Nguyễn Văn Test",
            PassengerPhone = "0912345678"
        });
        Assert.False(duplicateSeatCodes.Success);
        Assert.Contains("trùng lặp", duplicateSeatCodes.Message, StringComparison.OrdinalIgnoreCase);

        // Case 2: Passenger assigned a seat code not in request SeatCodes
        var seatNotInRequest = await service.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = holdToken,
            SeatCodes = seatCodes,
            PassengerName = "Nguyễn Văn Test",
            PassengerPhone = "0912345678",
            Passengers = new List<BookingPassengerDto>
            {
                new() { FullName = "Khách 1", PhoneNumber = "0912345678", SeatCode = seatCodes[0] },
                new() { FullName = "Khách 2", PhoneNumber = "0912345679", SeatCode = "NON-EXISTENT" }
            }
        });
        Assert.False(seatNotInRequest.Success);
        Assert.Contains("không nằm trong danh sách", seatNotInRequest.Message, StringComparison.OrdinalIgnoreCase);

        // Case 3: Two passengers assigned the same seat code
        var duplicateAssignment = await service.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = holdToken,
            SeatCodes = seatCodes,
            PassengerName = "Nguyễn Văn Test",
            PassengerPhone = "0912345678",
            Passengers = new List<BookingPassengerDto>
            {
                new() { FullName = "Khách 1", PhoneNumber = "0912345678", SeatCode = seatCodes[0] },
                new() { FullName = "Khách 2", PhoneNumber = "0912345679", SeatCode = seatCodes[0] }
            }
        });
        Assert.False(duplicateAssignment.Success);
        Assert.Contains("trùng lặp", duplicateAssignment.Message, StringComparison.OrdinalIgnoreCase);

        // Case 4: Companion passenger has short full name (< 2 chars)
        var shortNameResult = await service.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = holdToken,
            SeatCodes = seatCodes,
            PassengerName = "Nguyễn Văn Test",
            PassengerPhone = "0912345678",
            Passengers = new List<BookingPassengerDto>
            {
                new() { FullName = "Khách 1", PhoneNumber = "0912345678", SeatCode = seatCodes[0] },
                new() { FullName = "A", PhoneNumber = "0912345679", SeatCode = seatCodes[1] }
            }
        });
        Assert.False(shortNameResult.Success);
        Assert.Contains("2 ký tự", shortNameResult.Message, StringComparison.OrdinalIgnoreCase);

        // Case 5: Companion passenger has invalid email format
        var invalidEmailResult = await service.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = holdToken,
            SeatCodes = seatCodes,
            PassengerName = "Nguyễn Văn Test",
            PassengerPhone = "0912345678",
            Passengers = new List<BookingPassengerDto>
            {
                new() { FullName = "Khách 1", PhoneNumber = "0912345678", SeatCode = seatCodes[0] },
                new() { FullName = "Khách 2", PhoneNumber = "0912345679", Email = "invalid-email", SeatCode = seatCodes[1] }
            }
        });
        Assert.False(invalidEmailResult.Success);
        Assert.Contains("email", invalidEmailResult.Message, StringComparison.OrdinalIgnoreCase);

        // Case 6: Primary contact auto-correction when multiple passengers claim primary contact
        var multiPrimaryResult = await service.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = holdToken,
            SeatCodes = seatCodes,
            PassengerName = "Nguyễn Văn Test",
            PassengerPhone = "0912345678",
            Passengers = new List<BookingPassengerDto>
            {
                new() { FullName = "Khách 1", PhoneNumber = "0912345678", SeatCode = seatCodes[0], IsPrimaryContact = true },
                new() { FullName = "Khách 2", PhoneNumber = "0912345679", SeatCode = seatCodes[1], IsPrimaryContact = true }
            }
        });
        Assert.True(multiPrimaryResult.Success);
        var detail = await service.GetBookingByCodeAsync(multiPrimaryResult.BookingCode!);
        Assert.NotNull(detail);
        Assert.Single(detail.Passengers, p => p.IsPrimaryContact);
        Assert.Equal("Khách 1", detail.Passengers.Single(p => p.IsPrimaryContact).FullName);
    }

    // -------------------------------------------------------------
    // TEST 21: Idempotent Cancellation and Unauthorized Cancellation Rejection
    // -------------------------------------------------------------
    [Fact]
    public async Task Test21_Cancellation_IdempotencyAndUnauthorizedRejection()
    {
        string ownerA = $"OWNER-A-{Guid.NewGuid():N}";
        string ownerB = $"OWNER-B-{Guid.NewGuid():N}";

        await using var context = CreateIsolatedContext();
        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "IDEM");
        var repo = new BookingRepository(context);
        var serviceA = CreateService(repo, ownerA);
        var serviceB = CreateService(repo, ownerB);

        await serviceA.EnsureDemoTripInventoryAsync(trip.Id);
        var seat = (await serviceA.GetSeatsForTripAsync(trip.Id)).First();

        var hold = await serviceA.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seat.SeatCode },
            HoldMinutes = 10
        });
        Assert.True(hold.Success);

        var booking = await serviceA.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = hold.HoldToken!,
            SeatCodes = new List<string> { seat.SeatCode },
            PassengerName = "Chủ Đặt Chỗ A",
            PassengerPhone = "0911223344"
        });
        Assert.True(booking.Success);

        var bookingRecord = await context.Bookings.SingleAsync(b => b.BookingCode == booking.BookingCode);

        // User B tries to cancel User A's booking -> must fail
        var unauthorizedCancel = await serviceB.CancelBookingAsync(bookingRecord.Id, "Hacker B hủy trộm");
        Assert.False(unauthorizedCancel);

        // Booking status remains PendingPayment
        await context.Entry(bookingRecord).ReloadAsync();
        Assert.Equal(BookingStatus.PendingPayment, bookingRecord.Status);

        // User A cancels legitimately -> must succeed
        var legitimateCancel = await serviceA.CancelBookingAsync(bookingRecord.Id, "Hủy đổi lịch");
        Assert.True(legitimateCancel);

        // Idempotency: User A cancels again -> must return true without crashing or double-releasing
        var idempotentCancel = await serviceA.CancelBookingAsync(bookingRecord.Id, "Hủy lần hai");
        Assert.True(idempotentCancel);

        // Seat is Available
        var seatStatus = await context.TripSeats.AsNoTracking().SingleAsync(s => s.Id == seat.Id);
        Assert.Equal(TripSeatStatus.Available, seatStatus.Status);
    }

    // -------------------------------------------------------------
    // TEST 22: Booking Lookup Owner Isolation Enforced
    // -------------------------------------------------------------
    [Fact]
    public async Task Test22_BookingLookup_OwnerIsolation_Enforced()
    {
        string ownerA = $"OWNER-PRIV-A-{Guid.NewGuid():N}";
        string ownerB = $"OWNER-PRIV-B-{Guid.NewGuid():N}";

        await using var context = CreateIsolatedContext();
        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "PRIV");
        var repo = new BookingRepository(context);
        var serviceA = CreateService(repo, ownerA);
        var serviceB = CreateService(repo, ownerB);

        await serviceA.EnsureDemoTripInventoryAsync(trip.Id);
        var seat = (await serviceA.GetSeatsForTripAsync(trip.Id)).First();

        var hold = await serviceA.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seat.SeatCode },
            HoldMinutes = 10
        });
        Assert.True(hold.Success);

        var booking = await serviceA.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = hold.HoldToken!,
            SeatCodes = new List<string> { seat.SeatCode },
            PassengerName = "Bảo Mật A",
            PassengerPhone = "0988776655"
        });
        Assert.True(booking.Success);

        // User A can look up by code and by phone
        var lookupByCodeA = await serviceA.GetBookingByCodeAsync(booking.BookingCode!);
        Assert.NotNull(lookupByCodeA);
        Assert.Equal("Bảo Mật A", lookupByCodeA.PassengerName);

        var lookupByPhoneA = await serviceA.GetBookingsByPhoneAsync("0988776655");
        Assert.Single(lookupByPhoneA);

        // User B CANNOT look up User A's booking by code
        var lookupByCodeB = await serviceB.GetBookingByCodeAsync(booking.BookingCode!);
        Assert.Null(lookupByCodeB);

        // User B CANNOT look up User A's booking by phone
        var lookupByPhoneB = await serviceB.GetBookingsByPhoneAsync("0988776655");
        Assert.Empty(lookupByPhoneB);
    }

    // -------------------------------------------------------------
    // TEST 23: Expired Hold Token Rejected at Booking Creation
    // -------------------------------------------------------------
    [Fact]
    public async Task Test23_ExpiredHoldToken_RejectedAtBookingCreation()
    {
        string owner = $"OWNER-EXP-{Guid.NewGuid():N}";
        await using var context = CreateIsolatedContext();
        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "EXPH");
        var repo = new BookingRepository(context);
        var service = CreateService(repo, owner);

        await service.EnsureDemoTripInventoryAsync(trip.Id);
        var seat = (await service.GetSeatsForTripAsync(trip.Id)).First();

        var hold = await service.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seat.SeatCode },
            HoldMinutes = 10
        });
        Assert.True(hold.Success);

        // Artificially expire the hold in the database
        var holdRecord = await context.SeatHolds.SingleAsync(h => h.HoldToken == hold.HoldToken);
        holdRecord.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await context.SaveChangesAsync();

        // Attempt to create booking with the expired hold -> must fail
        var result = await service.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = hold.HoldToken!,
            SeatCodes = new List<string> { seat.SeatCode },
            PassengerName = "Khách Trễ Hạn",
            PassengerPhone = "0944332211"
        });

        Assert.False(result.Success);
        Assert.Contains("hết hạn", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------
    // TEST 24: Payment Success Workflow
    // -------------------------------------------------------------
    [Fact]
    public async Task Test24_PaymentSuccessWorkflow_ConfirmsBookingAndRecordsTransaction()
    {
        string owner = $"OWNER-PAY-{Guid.NewGuid():N}";
        await using var context = CreateIsolatedContext();
        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "PAYOK");
        var repo = new BookingRepository(context);
        var bookingService = CreateService(repo, owner);
        var paymentService = CreatePaymentService(repo, owner);

        await bookingService.EnsureDemoTripInventoryAsync(trip.Id);
        var seat = (await bookingService.GetSeatsForTripAsync(trip.Id)).First();

        var hold = await bookingService.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seat.SeatCode },
            HoldMinutes = 10
        });
        Assert.True(hold.Success);

        var booking = await bookingService.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = hold.HoldToken!,
            SeatCodes = new List<string> { seat.SeatCode },
            PassengerName = "Nguyễn Văn Thanh Toán",
            PassengerPhone = "0912345678"
        });
        Assert.True(booking.Success);
        Assert.NotNull(booking.BookingCode);

        // Process sandbox payment
        var payResult = await paymentService.ProcessPaymentAsync(new ProcessPaymentRequestDto
        {
            BookingCode = booking.BookingCode!,
            PaymentMethod = "MoMo Sandbox",
            SimulateSuccess = true,
            Amount = booking.TotalAmount
        });

        Assert.True(payResult.Success);
        Assert.NotNull(payResult.TransactionCode);
        Assert.Equal(PaymentStatus.Paid, payResult.Status);

        // Verify in database
        var dbBooking = await context.Bookings
            .Include(b => b.PaymentTransactions)
            .Include(b => b.BookingSeats)
            .SingleAsync(b => b.BookingCode == booking.BookingCode!);

        Assert.Equal(BookingStatus.Confirmed, dbBooking.Status);
        Assert.Equal(PaymentStatus.Paid, dbBooking.PaymentStatus);
        Assert.NotNull(dbBooking.ConfirmedAt);
        Assert.Single(dbBooking.PaymentTransactions);

        var queuedEmail = await context.BookingConfirmationEmailOutbox
            .SingleAsync(item => item.BookingId == dbBooking.Id &&
                                 item.MessageType == BookingConfirmationEmailOutbox.TypeBookingConfirmation);
        Assert.Equal(BookingConfirmationEmailOutbox.Pending, queuedEmail.Status);
        Assert.Equal("test@example.com", queuedEmail.RecipientEmail);
        Assert.Contains(booking.BookingCode!, queuedEmail.Payload);

        var emailSender = new RetryingEmailSender();
        await DeferOtherOutboxMessagesAsync(context, queuedEmail.Id);
        await RunConfirmationEmailWorkerAsync(emailSender);
        await context.Entry(queuedEmail).ReloadAsync();
        Assert.Equal(BookingConfirmationEmailOutbox.Pending, queuedEmail.Status);
        Assert.Equal(1, queuedEmail.AttemptCount);
        Assert.Equal("Simulated SMTP failure.", queuedEmail.LastError);

        queuedEmail.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await context.SaveChangesAsync();
        emailSender.ShouldFail = false;
        await RunConfirmationEmailWorkerAsync(emailSender);
        await context.Entry(queuedEmail).ReloadAsync();
        Assert.Equal(BookingConfirmationEmailOutbox.Sent, queuedEmail.Status);
        Assert.Equal(2, queuedEmail.AttemptCount);
        Assert.NotNull(queuedEmail.SentAt);
        Assert.Equal(2, emailSender.BookingConfirmationAttempts);

        var tx = dbBooking.PaymentTransactions.First();
        Assert.Equal(payResult.TransactionCode, tx.TransactionCode);
        Assert.Equal(PaymentStatus.Paid, tx.Status);
        Assert.True(tx.IsSandbox);
        Assert.Equal("MoMo Sandbox", tx.PaymentMethod);

        // Verify seat is still Booked
        var dbSeat = await context.TripSeats.SingleAsync(s => s.TripId == trip.Id && s.SeatCode == seat.SeatCode);
        Assert.Equal(TripSeatStatus.Booked, dbSeat.Status);
    }

    [Fact]
    public async Task BookingConfirmationOutbox_RecoversExpiredLeaseAndMarksRetryExhaustion()
    {
        await using var context = CreateIsolatedContext();
        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "MAIL");
        var now = DateTimeOffset.UtcNow;
        var booking = new Booking
        {
            BookingCode = $"MAIL-{_runMarker}-{Guid.NewGuid():N}"[..30],
            OwnerIdentifier = _defaultOwner,
            TripId = trip.Id,
            PassengerName = "Sandbox Outbox Test",
            PassengerPhone = "0900000000",
            PassengerEmail = "test@example.com",
            BaseAmount = 100_000m,
            TotalAmount = 100_000m,
            Status = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Paid,
            CreatedAt = now,
            ExpiresAt = now,
            ConfirmedAt = now,
            IsDemoBooking = true
        };
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        var payload = JsonSerializer.Serialize(new BookingConfirmationEmailDto
        {
            BookingCode = booking.BookingCode,
            PassengerName = booking.PassengerName,
            RecipientEmail = booking.PassengerEmail,
            RouteName = "Sandbox test route",
            OperatorName = "Sandbox test operator",
            DepartureTime = trip.DepartureTime,
            SeatCodes = new List<string> { "TEST-1" },
            TotalAmount = booking.TotalAmount
        });
        var outbox = new BookingConfirmationEmailOutbox
        {
            BookingId = booking.Id,
            RecipientEmail = booking.PassengerEmail,
            Payload = payload,
            Status = BookingConfirmationEmailOutbox.Processing,
            AttemptCount = 0,
            NextAttemptAt = now,
            LockedUntil = now.AddMinutes(-1),
            CreatedAt = now
        };
        context.BookingConfirmationEmailOutbox.Add(outbox);
        await context.SaveChangesAsync();

        var emailSender = new RetryingEmailSender();
        await DeferOtherOutboxMessagesAsync(context, outbox.Id);
        await RunConfirmationEmailWorkerAsync(emailSender);
        await context.Entry(outbox).ReloadAsync();
        Assert.Equal(BookingConfirmationEmailOutbox.Pending, outbox.Status);
        Assert.Null(outbox.LockedUntil);
        Assert.Equal(1, outbox.AttemptCount);

        outbox.AttemptCount = 4;
        outbox.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await context.SaveChangesAsync();
        await RunConfirmationEmailWorkerAsync(emailSender);
        await context.Entry(outbox).ReloadAsync();

        Assert.Equal(BookingConfirmationEmailOutbox.Failed, outbox.Status);
        Assert.Equal(5, outbox.AttemptCount);
        Assert.Null(outbox.LockedUntil);
        Assert.NotNull(outbox.LastError);
        Assert.Equal(2, emailSender.BookingConfirmationAttempts);
    }

    // -------------------------------------------------------------
    // TEST 25: Payment Failure Simulation
    // -------------------------------------------------------------
    [Fact]
    public async Task Test25_PaymentFailureSimulation_RecordsFailedTransaction_LeavesBookingPending()
    {
        string owner = $"OWNER-FAIL-{Guid.NewGuid():N}";
        await using var context = CreateIsolatedContext();
        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "PAYFAIL");
        var repo = new BookingRepository(context);
        var bookingService = CreateService(repo, owner);
        var paymentService = CreatePaymentService(repo, owner);

        await bookingService.EnsureDemoTripInventoryAsync(trip.Id);
        var seat = (await bookingService.GetSeatsForTripAsync(trip.Id)).First();

        var hold = await bookingService.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seat.SeatCode },
            HoldMinutes = 10
        });
        Assert.True(hold.Success);

        var booking = await bookingService.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = hold.HoldToken!,
            SeatCodes = new List<string> { seat.SeatCode },
            PassengerName = "Trần Thử Thất Bại",
            PassengerPhone = "0987654321"
        });
        Assert.True(booking.Success);

        // Process simulated failed payment
        var payResult = await paymentService.ProcessPaymentAsync(new ProcessPaymentRequestDto
        {
            BookingCode = booking.BookingCode!,
            PaymentMethod = "VNPAY Sandbox",
            SimulateSuccess = false,
            FailureReason = "Mô phỏng thanh toán thất bại bởi người dùng",
            Amount = booking.TotalAmount
        });

        Assert.False(payResult.Success);
        Assert.Contains("thất bại", payResult.Message, StringComparison.OrdinalIgnoreCase);

        // Verify in database: booking is still pending and unpaid
        var dbBooking = await context.Bookings
            .Include(b => b.PaymentTransactions)
            .SingleAsync(b => b.BookingCode == booking.BookingCode!);

        Assert.Equal(BookingStatus.PendingPayment, dbBooking.Status);
        Assert.Equal(PaymentStatus.Failed, dbBooking.PaymentStatus);
        Assert.Null(dbBooking.ConfirmedAt);
        Assert.Single(dbBooking.PaymentTransactions);

        var tx = dbBooking.PaymentTransactions.First();
        Assert.Equal(PaymentStatus.Failed, tx.Status);
    }

    // -------------------------------------------------------------
    // TEST 26: Duplicate / Idempotent Payment
    // -------------------------------------------------------------
    [Fact]
    public async Task Test26_DuplicatePayment_ReturnsIdempotentSuccess()
    {
        string owner = $"OWNER-IDEMP-{Guid.NewGuid():N}";
        await using var context = CreateIsolatedContext();
        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "IDEMP");
        var repo = new BookingRepository(context);
        var bookingService = CreateService(repo, owner);
        var paymentService = CreatePaymentService(repo, owner);

        await bookingService.EnsureDemoTripInventoryAsync(trip.Id);
        var seat = (await bookingService.GetSeatsForTripAsync(trip.Id)).First();

        var hold = await bookingService.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seat.SeatCode },
            HoldMinutes = 10
        });

        var booking = await bookingService.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = hold.HoldToken!,
            SeatCodes = new List<string> { seat.SeatCode },
            PassengerName = "Lê Trùng Lặp",
            PassengerPhone = "0933221100"
        });

        // First payment
        var firstPay = await paymentService.ProcessPaymentAsync(new ProcessPaymentRequestDto
        {
            BookingCode = booking.BookingCode!,
            PaymentMethod = "MoMo Sandbox",
            SimulateSuccess = true,
            Amount = booking.TotalAmount
        });
        Assert.True(firstPay.Success);

        // Second duplicate payment call
        var secondPay = await paymentService.ProcessPaymentAsync(new ProcessPaymentRequestDto
        {
            BookingCode = booking.BookingCode!,
            PaymentMethod = "MoMo Sandbox",
            SimulateSuccess = true,
            Amount = booking.TotalAmount
        });

        Assert.True(secondPay.Success);
        Assert.Equal(firstPay.TransactionCode, secondPay.TransactionCode);
        Assert.Equal(PaymentStatus.Paid, secondPay.Status);
    }

    // -------------------------------------------------------------
    // TEST 27: Payment Amount Mismatch Validation
    // -------------------------------------------------------------
    [Fact]
    public async Task Test27_PaymentAmountMismatch_RejectsTransaction()
    {
        string owner = $"OWNER-MISMATCH-{Guid.NewGuid():N}";
        await using var context = CreateIsolatedContext();
        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "MISMATCH");
        var repo = new BookingRepository(context);
        var bookingService = CreateService(repo, owner);
        var paymentService = CreatePaymentService(repo, owner);

        await bookingService.EnsureDemoTripInventoryAsync(trip.Id);
        var seat = (await bookingService.GetSeatsForTripAsync(trip.Id)).First();

        var hold = await bookingService.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seat.SeatCode },
            HoldMinutes = 10
        });

        var booking = await bookingService.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = hold.HoldToken!,
            SeatCodes = new List<string> { seat.SeatCode },
            PassengerName = "Hoàng Lệch Tiền",
            PassengerPhone = "0911223344"
        });

        // Pay with incorrect amount
        var payResult = await paymentService.ProcessPaymentAsync(new ProcessPaymentRequestDto
        {
            BookingCode = booking.BookingCode!,
            PaymentMethod = "MoMo Sandbox",
            SimulateSuccess = true,
            Amount = 1000m // Incorrect amount
        });

        Assert.False(payResult.Success);
        Assert.Contains("không khớp", payResult.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------
    // TEST 28: Unauthorized Session Cannot Pay Another User's Booking
    // -------------------------------------------------------------
    [Fact]
    public async Task Test28_UnauthorizedSession_CannotPayAnotherUsersBooking()
    {
        string ownerA = $"OWNER-A-{Guid.NewGuid():N}";
        string ownerB = $"OWNER-B-{Guid.NewGuid():N}";
        await using var context = CreateIsolatedContext();
        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "UNAUTH");
        var repo = new BookingRepository(context);
        var serviceA = CreateService(repo, ownerA);
        var paymentServiceB = CreatePaymentService(repo, ownerB);

        await serviceA.EnsureDemoTripInventoryAsync(trip.Id);
        var seat = (await serviceA.GetSeatsForTripAsync(trip.Id)).First();

        var hold = await serviceA.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seat.SeatCode },
            HoldMinutes = 10
        });

        var booking = await serviceA.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = hold.HoldToken!,
            SeatCodes = new List<string> { seat.SeatCode },
            PassengerName = "Phạm Sở Hữu A",
            PassengerPhone = "0944556677"
        });

        // User B tries to pay User A's booking
        var payResult = await paymentServiceB.ProcessPaymentAsync(new ProcessPaymentRequestDto
        {
            BookingCode = booking.BookingCode!,
            PaymentMethod = "MoMo Sandbox",
            SimulateSuccess = true,
            Amount = booking.TotalAmount
        });

        Assert.False(payResult.Success);
        Assert.Contains("quyền", payResult.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------
    // TEST 29: Booking Cancellation and Seat Release
    // -------------------------------------------------------------
    [Fact]
    public async Task Test29_BookingCancellation_ReleasesSeatsBackToAvailable()
    {
        string owner = $"OWNER-CANCEL-{Guid.NewGuid():N}";
        await using var context = CreateIsolatedContext();
        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "CANCEL");
        var repo = new BookingRepository(context);
        var bookingService = CreateService(repo, owner);

        await bookingService.EnsureDemoTripInventoryAsync(trip.Id);
        var seat = (await bookingService.GetSeatsForTripAsync(trip.Id)).First();

        var hold = await bookingService.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seat.SeatCode },
            HoldMinutes = 10
        });

        var booking = await bookingService.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = hold.HoldToken!,
            SeatCodes = new List<string> { seat.SeatCode },
            PassengerName = "Vũ Yêu Cầu Hủy",
            PassengerPhone = "0977889900"
        });
        Assert.True(booking.Success);

        var dbBooking = await context.Bookings.SingleAsync(b => b.BookingCode == booking.BookingCode!);

        // Cancel booking
        var cancelSuccess = await bookingService.CancelBookingAsync(dbBooking.Id, "Khách hàng đổi kế hoạch");
        Assert.True(cancelSuccess);

        // Verify booking status in database
        var updatedBooking = await context.Bookings.SingleAsync(b => b.Id == dbBooking.Id);
        Assert.Equal(BookingStatus.Cancelled, updatedBooking.Status);

        // Verify seat is released back to Available
        var dbSeat = await context.TripSeats.SingleAsync(s => s.TripId == trip.Id && s.SeatCode == seat.SeatCode);
        Assert.Equal(TripSeatStatus.Available, dbSeat.Status);
    }

    // -------------------------------------------------------------
    // TEST 30: Multi-Passenger Validation and Persistence
    // -------------------------------------------------------------
    [Fact]
    public async Task Test30_MultiPassengerValidation_PersistsAllPassengersCorrectly()
    {
        string owner = $"OWNER-MULTIP-{Guid.NewGuid():N}";
        await using var context = CreateIsolatedContext();
        var trip = await CreateTestTripAsync(context, isDemo: true, codePrefix: "MULTIP");
        var repo = new BookingRepository(context);
        var bookingService = CreateService(repo, owner);

        await bookingService.EnsureDemoTripInventoryAsync(trip.Id);
        var seats = (await bookingService.GetSeatsForTripAsync(trip.Id)).Take(2).ToList();
        Assert.Equal(2, seats.Count);

        var hold = await bookingService.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = seats.Select(s => s.SeatCode).ToList(),
            HoldMinutes = 10
        });
        Assert.True(hold.Success);

        // 1. Mismatched passenger count should fail validation
        var invalidReq = new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = hold.HoldToken!,
            SeatCodes = seats.Select(s => s.SeatCode).ToList(),
            PassengerName = "Nguyễn Đại Diện",
            PassengerPhone = "0911002233",
            Passengers = new List<BookingPassengerDto>
            {
                new() { FullName = "Hành Khách Duy Nhất", PhoneNumber = "0911002233", IsPrimaryContact = true }
            } // Only 1 passenger for 2 seats
        };
        var invalidResult = await bookingService.CreateBookingAsync(invalidReq);
        Assert.False(invalidResult.Success);
        Assert.Contains("khớp", invalidResult.Message, StringComparison.OrdinalIgnoreCase);

        // 2. Valid multi-passenger request should succeed
        var validReq = new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = hold.HoldToken!,
            SeatCodes = seats.Select(s => s.SeatCode).ToList(),
            PassengerName = "Nguyễn Đại Diện",
            PassengerPhone = "0911002233",
            Passengers = new List<BookingPassengerDto>
            {
                new() { FullName = "Nguyễn Đại Diện", PhoneNumber = "0911002233", IsPrimaryContact = true },
                new() { FullName = "Trần Người Đi Cùng", PhoneNumber = "0988776655", IsPrimaryContact = false }
            }
        };
        var validResult = await bookingService.CreateBookingAsync(validReq);
        Assert.True(validResult.Success);

        // Verify in database
        var dbBooking = await context.Bookings
            .Include(b => b.Passengers)
            .SingleAsync(b => b.BookingCode == validResult.BookingCode!);

        Assert.Equal(2, dbBooking.Passengers.Count);
        Assert.Contains(dbBooking.Passengers, p => p.FullName == "Nguyễn Đại Diện" && p.IsPrimaryContact);
        Assert.Contains(dbBooking.Passengers, p => p.FullName == "Trần Người Đi Cùng" && !p.IsPrimaryContact);
    }
}
