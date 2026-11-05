using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Tripgo.Application.DTOs;
using Tripgo.Application.Interfaces;
using Tripgo.Application.Interfaces.Persistence;
using Tripgo.Application.Services;
using Tripgo.Application.Services.Temporary;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;
using Tripgo.Infrastructure.Persistence;
using Tripgo.Infrastructure.Persistence.Repositories;
using Tripgo.Infrastructure.Services;
using Xunit;

namespace Tripgo.Tests;

[Collection("Database")]
public class AllImportedRoutesBookingTests : IAsyncLifetime
{
    private const string IsolatedConnectionString =
        "Server=localhost\\MSSQLSERVER01;Database=TripgoDb_IsolatedTest;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true;";

    private readonly string _testRunId = Guid.NewGuid().ToString("N")[..8];
    private readonly List<string> _createdBookingCodes = new();
    private readonly Xunit.Abstractions.ITestOutputHelper? _output;

    public AllImportedRoutesBookingTests(Xunit.Abstractions.ITestOutputHelper? output = null)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        await using var context = CreateIsolatedContext();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<SandboxFixtureService>.Instance;
        var fixtureService = new SandboxFixtureService(context, logger);
        // Ensure all active imported routes have sandbox departures, stops, and seats initialized
        await fixtureService.EnsureSandboxTripsAsync();
    }

    public async Task DisposeAsync()
    {
        if (_createdBookingCodes.Count == 0) return;

        await using var context = CreateIsolatedContext();
        var bookings = await context.Bookings
            .Include(b => b.BookingSeats)
            .Include(b => b.Passengers)
            .Where(b => _createdBookingCodes.Contains(b.BookingCode))
            .ToListAsync();

        var bookingIds = bookings.Select(b => b.Id).ToList();
        var seatIds = bookings.SelectMany(b => b.BookingSeats).Select(bs => bs.TripSeatId).Distinct().ToList();

        await context.PaymentTransactions.Where(p => bookingIds.Contains(p.BookingId)).ExecuteDeleteAsync();
        await context.BookingConfirmationEmailOutbox.Where(item => bookingIds.Contains(item.BookingId)).ExecuteDeleteAsync();
        await context.BookingPassengers.Where(p => bookingIds.Contains(p.BookingId)).ExecuteDeleteAsync();
        await context.BookingSeats.Where(s => bookingIds.Contains(s.BookingId)).ExecuteDeleteAsync();
        await context.Bookings.Where(b => bookingIds.Contains(b.Id)).ExecuteDeleteAsync();

        // Restore seats back to Available
        if (seatIds.Count > 0)
        {
            var seats = await context.TripSeats.Where(s => seatIds.Contains(s.Id)).ToListAsync();
            foreach (var seat in seats)
            {
                seat.Status = TripSeatStatus.Available;
                seat.UpdatedAt = DateTimeOffset.UtcNow;
            }
            await context.SaveChangesAsync();
        }
    }

    private AppDbContext CreateIsolatedContext()
    {
        var target = new SqlConnectionStringBuilder(IsolatedConnectionString);
        if (!string.Equals(target.InitialCatalog, "TripgoDb_IsolatedTest", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Integration tests may ONLY target TripgoDb_IsolatedTest.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(IsolatedConnectionString, sql => sql.EnableRetryOnFailure())
            .Options;
        return new AppDbContext(options);
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

    private (BookingService bookingService, PaymentService paymentService, TripService tripService) CreateServices(
        AppDbContext context, string ownerIdentifier)
    {
        var sessionContext = new FixedBookingSessionContext(ownerIdentifier);
        var bookingRepo = new BookingRepository(context);
        var tripRepo = new TripRepository(context);
        var bookingStore = new InMemoryTemporaryBookingStore();

        var bookingService = new BookingService(bookingRepo, sessionContext);
        var paymentService = new PaymentService(bookingRepo, sessionContext);
        var tripService = new TripService(tripRepo, bookingStore, bookingService);

        return (bookingService, paymentService, tripService);
    }

    [Fact]
    public async Task SafetyRule_RejectAnyTargetingOfTripgoDbProduction()
    {
        var dummyProdOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost\\MSSQLSERVER01;Database=TripgoDb;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;

        await using var prodContext = new AppDbContext(dummyProdOptions);
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<SandboxFixtureService>.Instance;
        var fixtureService = new SandboxFixtureService(prodContext, logger);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixtureService.EnsureSandboxTripsAsync());
    }

    [Fact]
    public async Task AllActiveImportedRoutes_HaveSufficientSandboxTripsAndInventory()
    {
        await using var context = CreateIsolatedContext();
        var activeRoutes = await context.Routes
            .Include(r => r.Stops)
                .ThenInclude(s => s.Point)
            .Include(r => r.Trips)
                .ThenInclude(t => t.Stops)
            .Include(r => r.Trips)
                .ThenInclude(t => t.Seats)
            .Where(r => r.IsActive)
            .OrderBy(r => r.Id)
            .ToListAsync();

        Assert.NotEmpty(activeRoutes);
        Assert.True(activeRoutes.Count >= 13, $"Expected at least 13 active routes, found {activeRoutes.Count}");

        foreach (var route in activeRoutes)
        {
            // Verify RouteStops have pickup & dropoff with valid ordering
            var pickupStops = route.Stops.Where(s => s.AllowPickup).OrderBy(s => s.StopOrder).ToList();
            var dropoffStops = route.Stops.Where(s => s.AllowDropoff).OrderBy(s => s.StopOrder).ToList();

            Assert.NotEmpty(pickupStops);
            Assert.NotEmpty(dropoffStops);
            Assert.True(pickupStops.First().StopOrder < dropoffStops.Last().StopOrder,
                $"Route {route.Code}: Pickup stop order must be strictly less than dropoff stop order.");

            // Verify upcoming sandbox trips exist
            var upcomingSandboxTrips = route.Trips
                .Where(t => t.IsDemoTrip && t.Status == TripStatus.Scheduled && t.DepartureTime > DateTimeOffset.UtcNow)
                .Where(t => t.TripCode.StartsWith("[SANDBOX]") || t.TripCode.StartsWith("DEMO-"))
                .ToList();

            Assert.NotEmpty(upcomingSandboxTrips);
            Assert.True(upcomingSandboxTrips.Count >= 3,
                $"Route {route.Code} should have at least 3 upcoming sandbox departures, found {upcomingSandboxTrips.Count}");

            foreach (var trip in upcomingSandboxTrips)
            {
                Assert.True(trip.TripCode.StartsWith("[SANDBOX]") || trip.TripCode.StartsWith("DEMO-"));
                Assert.True(trip.Stops.Count >= 2,
                    $"Trip {trip.TripCode} on route {route.Code} must have at least 2 TripStops.");
                Assert.True(trip.Seats.Count >= 10,
                    $"Trip {trip.TripCode} on route {route.Code} must have at least 10 TripSeats (found {trip.Seats.Count}).");
                Assert.True(trip.Seats.Any(s => s.Status == TripSeatStatus.Available),
                    $"Trip {trip.TripCode} on route {route.Code} must have Available seats.");
            }
        }
    }

    [Fact]
    public async Task EveryImportedRoute_SupportsFullEndToEndBookingFlow_SearchToPaymentAndConfirmation()
    {
        await using var context = CreateIsolatedContext();
        var activeRoutes = await context.Routes
            .Include(r => r.Operator)
            .Where(r => r.IsActive)
            .OrderBy(r => r.Id)
            .ToListAsync();

        Assert.NotEmpty(activeRoutes);

        var testDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));

        foreach (var route in activeRoutes)
        {
            var userOwnerId = $"user-{_testRunId}-route-{route.Id}";
            var otherOwnerId = $"attacker-{_testRunId}-route-{route.Id}";

            var (bookingService, paymentService, tripService) = CreateServices(context, userOwnerId);
            var (otherBookingService, _, _) = CreateServices(context, otherOwnerId);

            // STEP 1: Search Trips
            var searchRequest = new TripSearchRequestDto(route.OriginLocationId, route.DestinationLocationId, testDate);
            var searchResults = await tripService.SearchTripsAsync(searchRequest);
            Assert.NotEmpty(searchResults);

            var targetTrip = searchResults.FirstOrDefault(t => t.IsBookable);
            Assert.NotNull(targetTrip);
            Assert.True(targetTrip.PickupPoints.Count >= 1, $"Route {route.Code} search result must have pickup points.");
            Assert.True(targetTrip.DropoffPoints.Count >= 1, $"Route {route.Code} search result must have dropoff points.");

            // STEP 2 & 3: Load Seats & Hold Seat
            var seats = await bookingService.GetSeatsForTripAsync(targetTrip.Id);
            Assert.NotEmpty(seats);
            var availableSeat = seats.FirstOrDefault(s => s.Status == "Available");
            Assert.NotNull(availableSeat);

            var holdRequest = new SeatHoldRequestDto
            {
                TripId = targetTrip.Id,
                SeatCodes = new List<string> { availableSeat.SeatCode }
            };

            var holdResult = await bookingService.HoldSeatsAsync(holdRequest);
            Assert.True(holdResult.Success, $"Seat hold failed on route {route.Code}: {holdResult.Message}");
            Assert.False(string.IsNullOrWhiteSpace(holdResult.HoldToken));
            Assert.True(holdResult.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(8));

            // Verify double hold prevention: other user cannot hold the exact same seat
            var doubleHoldResult = await otherBookingService.HoldSeatsAsync(holdRequest);
            Assert.False(doubleHoldResult.Success, "Double hold should have failed for another user.");

            // STEP 4: Select Pickup and Dropoff
            var tripDetails = await bookingRepo_GetTripForBookingAsync(context, targetTrip.Id);
            Assert.NotNull(tripDetails);

            var pickupPoint = tripDetails.Stops.FirstOrDefault(s => s.AllowPickup)?.Point ??
                              tripDetails.Route?.Stops.FirstOrDefault(s => s.AllowPickup)?.Point;
            var dropoffPoint = tripDetails.Stops.FirstOrDefault(s => s.AllowDropoff)?.Point ??
                               tripDetails.Route?.Stops.FirstOrDefault(s => s.AllowDropoff)?.Point;

            Assert.NotNull(pickupPoint);
            Assert.NotNull(dropoffPoint);

            // STEP 5: Passenger validation check (invalid passenger)
            var invalidPassengerRequest = new TestCreateBookingRequestDto
            {
                TripId = targetTrip.Id,
                HoldToken = holdResult.HoldToken,
                SeatCodes = new List<string> { availableSeat.SeatCode },
                PickupPointId = pickupPoint.Id,
                DropoffPointId = dropoffPoint.Id,
                PassengerName = "", // Invalid
                PassengerPhone = "", // Invalid
                PassengerEmail = "invalid-email",
                Passengers = new List<BookingPassengerDto>
                {
                    new() { FullName = "", SeatCode = availableSeat.SeatCode, PhoneNumber = "" }
                }
            };
            var invalidResult = await bookingService.CreateBookingAsync(invalidPassengerRequest);
            Assert.False(invalidResult.Success, "Empty customer name and phone must be rejected.");

            // STEP 6: Create Booking with Valid Information
            var customerPhone = $"09{Random.Shared.Next(10000000, 99999999)}";
            var customerEmail = $"test.{_testRunId}.{route.Id}@tripgo-sandbox.test";
            var validRequest = new TestCreateBookingRequestDto
            {
                TripId = targetTrip.Id,
                HoldToken = holdResult.HoldToken,
                SeatCodes = new List<string> { availableSeat.SeatCode },
                PickupPointId = pickupPoint.Id,
                DropoffPointId = dropoffPoint.Id,
                PassengerName = $"Test Customer {route.Code}",
                PassengerPhone = customerPhone,
                PassengerEmail = customerEmail,
                Passengers = new List<BookingPassengerDto>
                {
                    new()
                    {
                        FullName = $"Test Passenger {availableSeat.SeatCode}",
                        PhoneNumber = customerPhone,
                        SeatCode = availableSeat.SeatCode,
                        IsPrimaryContact = true
                    }
                }
            };

            var bookingResult = await bookingService.CreateBookingAsync(validRequest);
            Assert.True(bookingResult.Success, $"Booking creation failed on route {route.Code}: {bookingResult.Message}");
            Assert.False(string.IsNullOrWhiteSpace(bookingResult.BookingCode));
            Assert.StartsWith("TRIPGO-", bookingResult.BookingCode);
            _createdBookingCodes.Add(bookingResult.BookingCode);

            // Verify initial booking state in DB
            var bookingEntity = await context.Bookings
                .Include(b => b.BookingSeats)
                    .ThenInclude(bs => bs.TripSeat)
                .Include(b => b.Passengers)
                .FirstOrDefaultAsync(b => b.BookingCode == bookingResult.BookingCode);

            Assert.NotNull(bookingEntity);
            Assert.Equal(BookingStatus.PendingPayment, bookingEntity.Status);
            Assert.Equal(PaymentStatus.Unpaid, bookingEntity.PaymentStatus);
            Assert.True(bookingEntity.TotalAmount >= targetTrip.BasePrice);

            // STEP 7: Sandbox Payment Simulation
            // 7a: Failed payment simulation
            var failPaymentRequest = new ProcessPaymentRequestDto
            {
                BookingCode = bookingResult.BookingCode,
                Amount = bookingEntity.TotalAmount,
                PaymentMethod = "SANDBOX_SIMULATED",
                SimulateSuccess = false,
                FailureReason = "Tài khoản thử nghiệm không đủ số dư"
            };
            var failResult = await paymentService.ProcessPaymentAsync(failPaymentRequest);
            Assert.False(failResult.Success);
            Assert.Equal(PaymentStatus.Failed, failResult.Status);

            // Booking must remain PendingPayment and PaymentStatus should reflect Failed
            await context.Entry(bookingEntity).ReloadAsync();
            Assert.Equal(BookingStatus.PendingPayment, bookingEntity.Status);
            Assert.Equal(PaymentStatus.Failed, bookingEntity.PaymentStatus);

            // 7b: Successful payment simulation
            var successPaymentRequest = new ProcessPaymentRequestDto
            {
                BookingCode = bookingResult.BookingCode,
                Amount = bookingEntity.TotalAmount,
                PaymentMethod = "SANDBOX_SIMULATED",
                SimulateSuccess = true
            };
            var successResult = await paymentService.ProcessPaymentAsync(successPaymentRequest);
            Assert.True(successResult.Success, $"Sandbox payment failed for booking {bookingResult.BookingCode}: {successResult.Message}");
            Assert.Equal(PaymentStatus.Paid, successResult.Status);

            // STEP 8: Confirmation & Persistence Verification
            await context.Entry(bookingEntity).ReloadAsync();
            Assert.Equal(BookingStatus.Confirmed, bookingEntity.Status);
            Assert.Equal(PaymentStatus.Paid, bookingEntity.PaymentStatus);

            var bookedSeat = await context.TripSeats.FindAsync(bookingEntity.BookingSeats.First().TripSeatId);
            Assert.NotNull(bookedSeat);
            Assert.Equal(TripSeatStatus.Booked, bookedSeat.Status);

            // STEP 9: My Tickets Lookup Verification
            var myBooking = await bookingService.GetBookingByCodeAsync(bookingResult.BookingCode);
            Assert.NotNull(myBooking);
            Assert.Equal(bookingResult.BookingCode, myBooking.BookingCode);
            Assert.Equal(customerPhone, myBooking.PassengerPhone);
            Assert.Equal("Confirmed", myBooking.Status);
            Assert.Equal("Paid", myBooking.PaymentStatus);
            Assert.NotEmpty(myBooking.SeatCodes);
            Assert.Contains(availableSeat.SeatCode, myBooking.SeatCodes);

            // Lookup by phone
            var phoneBookings = await bookingService.GetBookingsByPhoneAsync(customerPhone);
            Assert.NotEmpty(phoneBookings);
            Assert.Contains(phoneBookings, b => b.BookingCode == bookingResult.BookingCode);
        }
    }

    [Fact]
    public async Task BookingCancellation_ReleasesSeatBackToAvailable()
    {
        await using var context = CreateIsolatedContext();
        var route = await context.Routes
            .Include(r => r.Stops).ThenInclude(s => s.Point)
            .Include(r => r.Trips).ThenInclude(t => t.Stops)
            .FirstAsync(r => r.IsActive);

        var trip = await context.Trips
            .Include(t => t.Seats)
            .Include(t => t.Stops).ThenInclude(s => s.Point)
            .FirstAsync(t => t.RouteId == route.Id && t.IsDemoTrip && t.DepartureTime > DateTimeOffset.UtcNow);

        var ownerId = $"cancel-owner-{_testRunId}";
        var (bookingService, _, _) = CreateServices(context, ownerId);

        var seat = trip.Seats.First(s => s.Status == TripSeatStatus.Available);

        var hold = await bookingService.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seat.SeatCode }
        });
        Assert.True(hold.Success);

        var pickup = trip.Stops.First(s => s.AllowPickup).Point!;
        var dropoff = trip.Stops.First(s => s.AllowDropoff).Point!;

        var createResult = await bookingService.CreateBookingAsync(new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = hold.HoldToken!,
            SeatCodes = new List<string> { seat.SeatCode },
            PickupPointId = pickup.Id,
            DropoffPointId = dropoff.Id,
            PassengerName = "Cancel Test User",
            PassengerPhone = "0988776655",
            PassengerEmail = "cancel@test.com",
            Passengers = new List<BookingPassengerDto>
            {
                new() { FullName = "Cancel Passenger", PhoneNumber = "0988776655", SeatCode = seat.SeatCode, IsPrimaryContact = true }
            }
        });
        Assert.True(createResult.Success);
        _createdBookingCodes.Add(createResult.BookingCode!);

        var booking = await context.Bookings.FirstAsync(b => b.BookingCode == createResult.BookingCode);

        // Cancel the unpaid booking
        var cancelResult = await bookingService.CancelBookingAsync(booking.Id, "User cancelled test booking");
        Assert.True(cancelResult, "Cancellation must succeed for pending payment booking.");

        // Verify booking status is Cancelled
        await context.Entry(booking).ReloadAsync();
        Assert.Equal(BookingStatus.Cancelled, booking.Status);

        // Verify the seat is now released back to Available
        var reloadedSeat = await context.TripSeats.FindAsync(seat.Id);
        Assert.NotNull(reloadedSeat);
        Assert.Equal(TripSeatStatus.Available, reloadedSeat.Status);

        // Another user can now immediately hold that released seat
        var anotherOwnerId = $"another-{_testRunId}";
        var (anotherService, _, _) = CreateServices(context, anotherOwnerId);
        var reholdResult = await anotherService.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seat.SeatCode }
        });
        Assert.True(reholdResult.Success, "Released seat must be immediately holdable by another user.");

        // Release that hold
        await anotherService.ReleaseHoldAsync(reholdResult.HoldToken!);
    }

    [Fact]
    public async Task ExpiredSeatHold_IsCleanedUpAndReleasedAutomatically()
    {
        await using var context = CreateIsolatedContext();
        var trip = await context.Trips
            .Include(t => t.Seats)
            .FirstAsync(t => t.IsDemoTrip && t.DepartureTime > DateTimeOffset.UtcNow);

        var ownerId = $"expire-owner-{_testRunId}";
        var (bookingService, _, _) = CreateServices(context, ownerId);

        var seat = trip.Seats.First(s => s.Status == TripSeatStatus.Available);

        var hold = await bookingService.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seat.SeatCode }
        });
        Assert.True(hold.Success);

        // Manually artificially expire the hold in the DB
        var dbHold = await context.SeatHolds.FirstAsync(h => h.HoldToken == hold.HoldToken);
        dbHold.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await context.SaveChangesAsync();

        // Calling GetSeatsForTripAsync triggers hold cleanup
        var refreshedSeats = await bookingService.GetSeatsForTripAsync(trip.Id);
        var targetSeat = refreshedSeats.First(s => s.SeatCode == seat.SeatCode);
        Assert.Equal("Available", targetSeat.Status);

        // Another user can hold it
        var newOwnerId = $"new-user-{_testRunId}";
        var (newService, _, _) = CreateServices(context, newOwnerId);
        var newHold = await newService.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seat.SeatCode }
        });
        Assert.True(newHold.Success, "Seat with expired hold must be holdable by new user.");
        await newService.ReleaseHoldAsync(newHold.HoldToken!);
    }

    [Fact]
    public async Task DemonstrateThreeKeyRoutes_IncludingHaNoiToSaPa()
    {
        await using var context = CreateIsolatedContext();
        var targetRouteCodes = new[] { "SAOVIET-HAN-SAP", "TRONGMINH-SGN-DLI", "FUTA-HAN-DAD" };
        var testDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));

        foreach (var routeCode in targetRouteCodes)
        {
            var route = await context.Routes
                .Include(r => r.Operator)
                .Include(r => r.OriginLocation)
                .Include(r => r.DestinationLocation)
                .FirstAsync(r => r.Code == routeCode);

            _output?.WriteLine($"=== DEMONSTRATING ROUTE: {route.Code} ({route.OriginLocation.Name} -> {route.DestinationLocation.Name}) by {route.Operator.Name} ===");

            var ownerId = $"demo-{route.Code}-{_testRunId}";
            var (bookingService, paymentService, tripService) = CreateServices(context, ownerId);

            // Step 1: Search Trips
            var searchRequest = new TripSearchRequestDto(route.OriginLocationId, route.DestinationLocationId, testDate);
            var searchResults = await tripService.SearchTripsAsync(searchRequest);
            Assert.NotEmpty(searchResults);
            var targetTrip = searchResults.First(t => t.IsBookable);
            _output?.WriteLine($"[STEP 1 - SEARCH] Found Trip: {targetTrip.TripCode}, Operator: {targetTrip.OperatorName}, BasePrice: {targetTrip.BasePrice:N0} đ, Available Seats: {targetTrip.AvailableSeats}");

            // Step 2: Stops availability
            Assert.NotEmpty(targetTrip.PickupPoints);
            Assert.NotEmpty(targetTrip.DropoffPoints);
            var pickup = targetTrip.PickupPoints.First();
            var dropoff = targetTrip.DropoffPoints.First();
            _output?.WriteLine($"[STEP 2 - STOPS] Pickup: {pickup.Name} ({pickup.Address}) | Dropoff: {dropoff.Name} ({dropoff.Address})");

            // Step 3: Seat selection and holding
            var seats = await bookingService.GetSeatsForTripAsync(targetTrip.Id);
            var seat = seats.First(s => s.Status == "Available");
            var holdResult = await bookingService.HoldSeatsAsync(new SeatHoldRequestDto
            {
                TripId = targetTrip.Id,
                SeatCodes = new List<string> { seat.SeatCode }
            });
            Assert.True(holdResult.Success);
            _output?.WriteLine($"[STEP 3 - SEAT HOLD] Held Seat {seat.SeatCode}, Token: {holdResult.HoldToken?[..16]}..., ExpiresAt: {holdResult.ExpiresAt:HH:mm:ss}");

            // Step 4 & 5: Passenger Entry & Booking Creation
            var phone = $"097{Random.Shared.Next(1000000, 9999999)}";
            var bookingResult = await bookingService.CreateBookingAsync(new TestCreateBookingRequestDto
            {
                TripId = targetTrip.Id,
                HoldToken = holdResult.HoldToken!,
                SeatCodes = new List<string> { seat.SeatCode },
                PickupPointId = pickup.Id,
                DropoffPointId = dropoff.Id,
                PassengerName = $"Khách Hàng {route.Code}",
                PassengerPhone = phone,
                PassengerEmail = $"customer.{routeCode.ToLower()}@test.vn",
                Passengers = new List<BookingPassengerDto>
                {
                    new() { FullName = $"Hành Khách {seat.SeatCode}", PhoneNumber = phone, SeatCode = seat.SeatCode, IsPrimaryContact = true }
                }
            });
            Assert.True(bookingResult.Success);
            Assert.StartsWith("TRIPGO-", bookingResult.BookingCode);
            _createdBookingCodes.Add(bookingResult.BookingCode!);
            _output?.WriteLine($"[STEP 4 & 5 - BOOKING] Created Booking: {bookingResult.BookingCode}, Total: {bookingResult.TotalAmount:N0} đ, Status: PendingPayment");

            // Step 6 & 7: Review & Payment Simulation
            var payResult = await paymentService.ProcessPaymentAsync(new ProcessPaymentRequestDto
            {
                BookingCode = bookingResult.BookingCode,
                Amount = bookingResult.TotalAmount,
                PaymentMethod = "SANDBOX_SIMULATED",
                SimulateSuccess = true
            });
            Assert.True(payResult.Success);
            _output?.WriteLine($"[STEP 6 & 7 - PAYMENT] TxCode: {payResult.TransactionCode}, Status: {payResult.Status}");

            // Step 8 & 9: Confirmation & My Tickets Retrieval
            var confirmedBooking = await bookingService.GetBookingByCodeAsync(bookingResult.BookingCode!);
            Assert.NotNull(confirmedBooking);
            Assert.Equal("Confirmed", confirmedBooking.Status);
            Assert.Equal("Paid", confirmedBooking.PaymentStatus);
            Assert.Contains(seat.SeatCode, confirmedBooking.SeatCodes);

            var phoneLookup = await bookingService.GetBookingsByPhoneAsync(phone);
            Assert.Contains(phoneLookup, b => b.BookingCode == bookingResult.BookingCode);
            _output?.WriteLine($"[STEP 8 & 9 - MY TICKETS] Verified ticket {confirmedBooking.BookingCode} owned by {confirmedBooking.PassengerName} ({phone}) is CONFIRMED and PAID.\n");
        }
    }

    [Fact]
    public async Task SearchHaNoiToSaPa_ReturnsValidSandboxTrips()
    {
        await using var context = CreateIsolatedContext();
        var (bookingService, paymentService, tripService) = CreateServices(context, "test-owner");

        var locations = await tripService.GetLocationsAsync();
        var haNoi = locations.FirstOrDefault(l => l.Code == "HAN" || l.Province.Contains("Hà Nội") || l.Name.Contains("Hà Nội"));
        var saPa = locations.FirstOrDefault(l => l.Code == "SAP" || l.Province.Contains("Lào Cai") || l.Name.Contains("Sa Pa"));

        Assert.NotNull(haNoi);
        Assert.NotNull(saPa);
        Assert.Equal("HAN", haNoi.Code);
        Assert.Equal("SAP", saPa.Code);

        var vnOffset = TimeSpan.FromHours(7);
        var tomorrow = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(vnOffset).Date.AddDays(1));

        var request = new TripSearchRequestDto
        {
            OriginLocationId = haNoi.Id,
            DestinationLocationId = saPa.Id,
            DepartureDate = tomorrow
        };

        var results = await tripService.SearchTripsAsync(request);
        _output?.WriteLine($"Search results count: {results.Count}");
        foreach (var r in results)
        {
            _output?.WriteLine($"Trip: {r.TripCode}, Route: {r.RouteName}, Departure: {r.DepartureTime:yyyy-MM-dd HH:mm:ss zzz}, Available Seats: {r.AvailableSeats}");
        }

        Assert.NotEmpty(results);
        var sandboxTrips = results.Where(r => r.IsBookable).ToList();
        Assert.NotEmpty(sandboxTrips);
        Assert.True(sandboxTrips.Count >= 3, $"Expected at least 3 bookable sandbox trips, found {sandboxTrips.Count}");
        Assert.All(sandboxTrips, r => Assert.True(r.AvailableSeats > 0));

        var referenceTrips = results.Where(r => !r.IsBookable).ToList();
        Assert.NotEmpty(referenceTrips);
        Assert.All(referenceTrips, r => Assert.Equal("PUBLISHED_REFERENCE_SCHEDULE", r.OperationalTier));
    }

    private static async Task<Trip?> bookingRepo_GetTripForBookingAsync(AppDbContext context, long tripId)
    {
        var repo = new BookingRepository(context);
        return await repo.GetTripForBookingAsync(tripId);
    }
}
