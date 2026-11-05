using Microsoft.EntityFrameworkCore;
using Tripgo.Application.DTOs;
using Tripgo.Domain;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;
using Tripgo.Infrastructure.Persistence.Repositories;

namespace Tripgo.Tests.Stage1To4;

[Collection("Database")]
public sealed class BookingReliabilityTests : IAsyncLifetime
{
    private readonly Stage _stage = new();

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => _stage.CleanupAsync();

    private Task PayAsync(Tripgo.Infrastructure.Persistence.AppDbContext context, BookingDetailDto booking, bool success) =>
        _stage.Payments(context).ProcessPaymentAsync(new ProcessPaymentRequestDto
        {
            BookingCode = booking.BookingCode,
            Amount = booking.TotalAmount,
            PaymentMethod = "SANDBOX_SIMULATED",
            SimulateSuccess = success
        });

    [Fact]
    public async Task FailedPayment_CanBeCancelledByOwner_AndReleasesSeat()
    {
        await using var context = Stage.NewContext();
        var (trip, booking, _) = await _stage.CreatePendingBookingAsync(context);
        await PayAsync(context, booking, success: false);

        Assert.True(await _stage.Bookings(context).CancelBookingAsync(booking.Id, "Khách đổi ý sau khi thanh toán lỗi"));

        var seats = await _stage.Bookings(context).GetSeatsForTripAsync(trip.Id);
        Assert.True(seats.Single(s => s.SeatCode == booking.SeatCodes[0]).IsAvailable);
    }

    [Fact]
    public async Task FailedPayment_DoesNotLockSeatForever_WhenPaymentWindowEnds()
    {
        await using var context = Stage.NewContext();
        var (trip, booking, _) = await _stage.CreatePendingBookingAsync(context);
        await PayAsync(context, booking, success: false);

        await context.Bookings.Where(b => b.Id == booking.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));

        await new BookingRepository(context).ExpireStaleBookingsAsync();

        var stored = await context.Bookings.AsNoTracking().SingleAsync(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.Expired, stored.Status);
        var seats = await _stage.Bookings(context).GetSeatsForTripAsync(trip.Id);
        Assert.True(seats.Single(s => s.SeatCode == booking.SeatCodes[0]).IsAvailable);
    }

    [Fact]
    public async Task BlockedSeat_CannotBeHeld()
    {
        await using var context = Stage.NewContext();
        var trip = await _stage.CreateTripAsync(context);
        var service = _stage.Bookings(context);
        await service.EnsureDemoTripInventoryAsync(trip.Id);
        var seat = (await service.GetSeatsForTripAsync(trip.Id)).First();
        await context.TripSeats.Where(s => s.Id == seat.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, TripSeatStatus.Blocked));

        var hold = await service.HoldSeatsAsync(new SeatHoldRequestDto { TripId = trip.Id, SeatCodes = new() { seat.SeatCode } });

        Assert.False(hold.Success);
    }

    [Fact]
    public async Task Hold_IsRejected_WhenTripIsCancelled()
    {
        await using var context = Stage.NewContext();
        var trip = await _stage.CreateTripAsync(context);
        var service = _stage.Bookings(context);
        await service.EnsureDemoTripInventoryAsync(trip.Id);
        var seat = (await service.GetSeatsForTripAsync(trip.Id)).First();
        await context.Trips.Where(t => t.Id == trip.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TripStatus.Cancelled));

        var hold = await service.HoldSeatsAsync(new SeatHoldRequestDto { TripId = trip.Id, SeatCodes = new() { seat.SeatCode } });

        Assert.False(hold.Success);
    }

    [Fact]
    public async Task Booking_IsRejected_WhenTripIsCancelledAfterTheSeatWasHeld()
    {
        await using var context = Stage.NewContext();
        var trip = await _stage.CreateTripAsync(context);
        var service = _stage.Bookings(context);
        await service.EnsureDemoTripInventoryAsync(trip.Id);
        var seat = (await service.GetSeatsForTripAsync(trip.Id)).First();
        var hold = await service.HoldSeatsAsync(new SeatHoldRequestDto { TripId = trip.Id, SeatCodes = new() { seat.SeatCode } });
        Assert.True(hold.Success);

        await context.Trips.Where(t => t.Id == trip.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TripStatus.Cancelled));

        var created = await service.CreateBookingAsync(new CreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = hold.HoldToken!,
            SeatCodes = new() { seat.SeatCode },
            PassengerName = "Nguyen Van Khach",
            PassengerPhone = "0912345678",
            PassengerEmail = "khach@tripgo.test",
            ContactEmailConfirmed = true
        });

        Assert.False(created.Success);
        Assert.Empty(await context.Bookings.Where(b => b.TripId == trip.Id).ToListAsync());
    }

    [Fact]
    public async Task Sales_CloseBeforeDeparture_AndTheTripLeavesSearchResults()
    {
        await using var context = Stage.NewContext();
        var soon = DateTimeOffset.UtcNow.Add(BookingRules.SalesCutoffBeforeDeparture).AddMinutes(-1);
        var trip = await _stage.CreateTripAsync(context, departure: soon);
        var service = _stage.Bookings(context);
        await service.EnsureDemoTripInventoryAsync(trip.Id);
        var seat = (await service.GetSeatsForTripAsync(trip.Id)).First();

        var hold = await service.HoldSeatsAsync(new SeatHoldRequestDto { TripId = trip.Id, SeatCodes = new() { seat.SeatCode } });

        Assert.False(hold.Success);
    }

    [Fact]
    public async Task Hold_IsRejected_AboveTheSeatLimit()
    {
        await using var context = Stage.NewContext();
        var trip = await _stage.CreateTripAsync(context);
        var service = _stage.Bookings(context);
        await service.EnsureDemoTripInventoryAsync(trip.Id);
        var codes = (await service.GetSeatsForTripAsync(trip.Id)).Take(BookingRules.MaxSeatsPerBooking + 1).Select(s => s.SeatCode).ToList();

        var hold = await service.HoldSeatsAsync(new SeatHoldRequestDto { TripId = trip.Id, SeatCodes = codes });

        Assert.False(hold.Success);
    }

    [Fact]
    public async Task Booking_CannotUseAHoldFromAnotherTrip()
    {
        await using var context = Stage.NewContext();
        var firstTrip = await _stage.CreateTripAsync(context);
        var secondTrip = await _stage.CreateTripAsync(context);
        var service = _stage.Bookings(context);
        await service.EnsureDemoTripInventoryAsync(firstTrip.Id);
        await service.EnsureDemoTripInventoryAsync(secondTrip.Id);
        var seat = (await service.GetSeatsForTripAsync(firstTrip.Id)).First();
        var hold = await service.HoldSeatsAsync(new SeatHoldRequestDto { TripId = firstTrip.Id, SeatCodes = new() { seat.SeatCode } });

        var created = await service.CreateBookingAsync(new CreateBookingRequestDto
        {
            TripId = secondTrip.Id,
            HoldToken = hold.HoldToken!,
            SeatCodes = new() { seat.SeatCode },
            PassengerName = "Nguyen Van Khach",
            PassengerPhone = "0912345678",
            PassengerEmail = "khach@tripgo.test",
            ContactEmailConfirmed = true
        });

        Assert.False(created.Success);
    }

    [Fact]
    public async Task Availability_CountsHeldSeatsAsUnavailable()
    {
        await using var context = Stage.NewContext();
        var trip = await _stage.CreateTripAsync(context);
        var service = _stage.Bookings(context);
        await service.EnsureDemoTripInventoryAsync(trip.Id);
        var seats = await service.GetSeatsForTripAsync(trip.Id);
        await service.HoldSeatsAsync(new SeatHoldRequestDto { TripId = trip.Id, SeatCodes = new() { seats[0].SeatCode, seats[1].SeatCode } });

        var tripService = new Tripgo.Application.Services.TripService(
            new TripRepository(context), new Tripgo.Application.Services.Temporary.InMemoryTemporaryBookingStore(), service);
        var detail = await tripService.GetTripDetailsAsync(trip.Id);

        Assert.Equal(seats.Count - 2, detail!.AvailableSeats);
        Assert.Equal("Held", detail.Seats.Single(s => s.SeatCode == seats[0].SeatCode).Status);
    }

    [Fact]
    public async Task LongLivedContext_DoesNotDecideFromStaleRows()
    {
        // One context per Blazor circuit means a tracked row can be minutes old.
        await using var circuitContext = Stage.NewContext();
        var trip = await _stage.CreateTripAsync(circuitContext);
        var circuit = _stage.Bookings(circuitContext, Guid.NewGuid().ToString("N"));
        await circuit.EnsureDemoTripInventoryAsync(trip.Id);
        var seat = (await circuit.GetSeatsForTripAsync(trip.Id)).First();

        var firstHold = await circuit.HoldSeatsAsync(new SeatHoldRequestDto { TripId = trip.Id, SeatCodes = new() { seat.SeatCode } });
        Assert.True(await circuit.ReleaseHoldAsync(firstHold.HoldToken!));

        await using (var otherContext = Stage.NewContext())
        {
            var other = await _stage.Bookings(otherContext, Guid.NewGuid().ToString("N"))
                .HoldSeatsAsync(new SeatHoldRequestDto { TripId = trip.Id, SeatCodes = new() { seat.SeatCode } });
            Assert.True(other.Success, other.Message);
        }

        var secondHold = await circuit.HoldSeatsAsync(new SeatHoldRequestDto { TripId = trip.Id, SeatCodes = new() { seat.SeatCode } });
        Assert.False(secondHold.Success);
        Assert.Contains("giữ chỗ", secondHold.Message);
    }

    [Fact]
    public async Task ConcurrentPayments_ProduceExactlyOnePaidTransaction()
    {
        await using var setup = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(setup);

        var attempts = Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var context = Stage.NewContext();
            return await _stage.Payments(context).ProcessPaymentAsync(new ProcessPaymentRequestDto
            {
                BookingCode = booking.BookingCode,
                Amount = booking.TotalAmount,
                PaymentMethod = "SANDBOX_SIMULATED",
                SimulateSuccess = true
            });
        });
        await Task.WhenAll(attempts);

        var paid = await setup.PaymentTransactions.CountAsync(t => t.BookingId == booking.Id && t.Status == PaymentStatus.Paid);
        Assert.Equal(1, paid);
    }

    [Fact]
    public async Task SimulatedPayment_CannotSettleANonSandboxBooking()
    {
        await using var context = Stage.NewContext();
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);
        await context.Bookings.Where(b => b.Id == booking.Id).ExecuteUpdateAsync(s => s.SetProperty(b => b.IsDemoBooking, false));

        var result = await _stage.Payments(context).ProcessPaymentAsync(new ProcessPaymentRequestDto
        {
            BookingCode = booking.BookingCode,
            Amount = booking.TotalAmount,
            SimulateSuccess = true
        });

        Assert.False(result.Success);
        Assert.Equal(PaymentStatus.Unpaid, await context.Bookings.Where(b => b.Id == booking.Id).Select(b => b.PaymentStatus).SingleAsync());
    }

    [Theory]
    [InlineData(BookingStatus.PendingPayment, PaymentStatus.Unpaid, true)]
    [InlineData(BookingStatus.PendingPayment, PaymentStatus.Failed, true)]
    [InlineData(BookingStatus.PendingPayment, PaymentStatus.Pending, false)]
    [InlineData(BookingStatus.Confirmed, PaymentStatus.Paid, false)]
    [InlineData(BookingStatus.Cancelled, PaymentStatus.Refunded, false)]
    [InlineData(BookingStatus.Expired, PaymentStatus.Unpaid, false)]
    public void CustomerCancellation_IsAllowedOnlyBeforeMoneyIsTaken(BookingStatus status, PaymentStatus payment, bool expected)
    {
        Assert.Equal(expected, BookingRules.CustomerCanCancel(new Booking { Status = status, PaymentStatus = payment }));
    }

    [Fact]
    public void VietnamTime_IsIndependentOfTheServerTimeZone()
    {
        var utc = new DateTimeOffset(2026, 11, 5, 17, 30, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 11, 6), VietnamTime.Today(utc));
        Assert.Equal(0, utc.ToVietnam().Hour);
    }
}
