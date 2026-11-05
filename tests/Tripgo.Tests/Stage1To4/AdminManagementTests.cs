using Microsoft.EntityFrameworkCore;
using Tripgo.Application.DTOs;
using Tripgo.Application.DTOs.Admin;
using Tripgo.Application.Interfaces;
using Tripgo.Application.Services;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;

namespace Tripgo.Tests.Stage1To4;

[Collection("Database")]
public sealed class AdminManagementTests : IAsyncLifetime
{
    private readonly Stage _stage = new();

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => _stage.CleanupAsync();

    private async Task<(Tripgo.Infrastructure.Persistence.AppDbContext Context, AdminService Admin, long AdminId)> AdminAsync()
    {
        var context = Stage.NewContext();
        var adminId = await _stage.CreateUserAsync(context, RoleNames.Admin);
        return (context, _stage.Admin(context, adminId), adminId);
    }

    private async Task<(long OperatorId, long RouteId, long BusId)> NetworkAsync(AdminService admin, Tripgo.Infrastructure.Persistence.AppDbContext context, string suffix = "A")
    {
        var op = await admin.SaveOperatorAsync(new AdminOperatorInput { Code = $"OP{suffix}{_stage.Marker}", Name = $"Sandbox Op {suffix} {_stage.Marker}" });
        Assert.True(op.Success, op.Message);
        var locations = await admin.GetLocationsAsync();
        var route = await admin.SaveRouteAsync(new AdminRouteInput
        {
            OperatorId = op.Id!.Value,
            OriginLocationId = locations[0].Id,
            DestinationLocationId = locations[1].Id,
            Code = $"R{suffix}{_stage.Marker}",
            Name = "Tuyến thử nghiệm",
            DistanceKm = 100,
            EstimatedDurationMinutes = 120
        });
        Assert.True(route.Success, route.Message);
        var busTypeId = await context.BusTypes.Select(t => t.Id).FirstAsync();
        var bus = await admin.CreateBusAsync(new CreateBusDto
        {
            OperatorId = op.Id.Value,
            BusTypeId = busTypeId,
            LicensePlate = $"P{suffix}{_stage.Marker}",
            SeatLayoutDefinition = "A01,1,1,1,Seat\nA02,1,1,2,Seat"
        });
        Assert.True(bus.Success, bus.Message);
        return (op.Id.Value, route.Id!.Value, bus.Id!.Value);
    }

    private static AdminTripInput TripInput(long routeId, long busId, int daysAhead = 40, decimal price = 200_000m)
    {
        var departure = DateTimeOffset.UtcNow.AddDays(daysAhead);
        return new AdminTripInput
        {
            RouteId = routeId,
            BusId = busId,
            DepartureTime = departure,
            ExpectedArrivalTime = departure.AddHours(3),
            BasePrice = price
        };
    }

    private async Task MarkTripCodesForCleanupAsync(Tripgo.Infrastructure.Persistence.AppDbContext context, long routeId)
    {
        var trips = await context.Trips.Where(t => t.RouteId == routeId).ToListAsync();
        foreach (var trip in trips)
        {
            trip.TripCode = $"ST-{_stage.Marker}-{trip.Id}";
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    // ---------------------------------------------------------------- authorization

    [Fact]
    public async Task Anonymous_IsRejectedByTheService_NotOnlyByTheUi()
    {
        await using var context = Stage.NewContext();
        var admin = _stage.Admin(context, userId: null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => admin.GetDashboardAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => admin.CancelBookingAsync(1, "Không được phép"));
    }

    [Theory]
    [InlineData(RoleNames.Customer)]
    [InlineData(RoleNames.Operator)]
    public async Task NonAdminRoles_AreRejectedByTheService(string role)
    {
        await using var context = Stage.NewContext();
        var userId = await _stage.CreateUserAsync(context, role);
        var admin = _stage.Admin(context, userId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => admin.GetOperatorsAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            admin.SaveOperatorAsync(new AdminOperatorInput { Code = "X", Name = "X" }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => admin.GetBookingsAsync(new AdminBookingFilter()));
    }

    [Fact]
    public async Task DeactivatedAdmin_LosesAccessImmediately_WhateverTheirCookieSays()
    {
        await using var context = Stage.NewContext();
        var adminId = await _stage.CreateUserAsync(context, RoleNames.Admin, isActive: false);
        var admin = _stage.Admin(context, adminId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => admin.GetDashboardAsync());
    }

    // ---------------------------------------------------------------- operators

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ngắn")]
    public async Task OperatorVerification_RequiresRealEvidence(string? evidence)
    {
        var (context, admin, _) = await AdminAsync();
        await using var _ = context;
        var op = await admin.SaveOperatorAsync(new AdminOperatorInput { Code = $"V{_stage.Marker}", Name = "Op" });

        var result = await admin.SetOperatorVerificationAsync(op.Id!.Value, true, evidence);

        Assert.False(result.Success);
        Assert.False(await context.Operators.Where(o => o.Id == op.Id).Select(o => o.IsVerified).SingleAsync());
    }

    [Fact]
    public async Task OperatorVerification_RecordsWhoWhenAndWhy_AndIsAudited()
    {
        var (context, admin, adminId) = await AdminAsync();
        await using var _ = context;
        var op = await admin.SaveOperatorAsync(new AdminOperatorInput { Code = $"W{_stage.Marker}", Name = "Op" });
        Assert.False(await context.Operators.Where(o => o.Id == op.Id).Select(o => o.IsVerified).SingleAsync());

        var result = await admin.SetOperatorVerificationAsync(op.Id!.Value, true, "Giấy phép kinh doanh số TEST-123");

        Assert.True(result.Success);
        var stored = await context.Operators.AsNoTracking().SingleAsync(o => o.Id == op.Id);
        Assert.True(stored.IsVerified);
        Assert.Equal(adminId, stored.VerifiedByUserId);
        Assert.NotNull(stored.VerifiedAt);
        Assert.Equal(TransportDataSource.OperatorVerified, stored.DataSource);
        Assert.True(await context.AdminAuditLogs.AnyAsync(a => a.Action == "Operator.Verify" && a.ActorUserId == adminId));
    }

    [Fact]
    public async Task OperatorCodes_MustBeUnique()
    {
        var (context, admin, _) = await AdminAsync();
        await using var _ = context;
        var input = new AdminOperatorInput { Code = $"D{_stage.Marker}", Name = "Op" };
        Assert.True((await admin.SaveOperatorAsync(input)).Success);

        var duplicate = await admin.SaveOperatorAsync(new AdminOperatorInput { Code = $"D{_stage.Marker}", Name = "Op 2" });

        Assert.False(duplicate.Success);
    }

    // ---------------------------------------------------------------- routes and stops

    [Fact]
    public async Task DuplicateActiveRoute_IsRejected()
    {
        var (context, admin, _) = await AdminAsync();
        await using var _ = context;
        var (operatorId, _, _) = await NetworkAsync(admin, context);
        var locations = await admin.GetLocationsAsync();

        var duplicate = await admin.SaveRouteAsync(new AdminRouteInput
        {
            OperatorId = operatorId,
            OriginLocationId = locations[0].Id,
            DestinationLocationId = locations[1].Id,
            Code = $"X{_stage.Marker}",
            Name = "Trùng",
            DistanceKm = 100,
            EstimatedDurationMinutes = 120
        });

        Assert.False(duplicate.Success);
    }

    [Fact]
    public async Task Route_WithSameOriginAndDestination_IsRejected()
    {
        var (context, admin, _) = await AdminAsync();
        await using var _ = context;
        var op = await admin.SaveOperatorAsync(new AdminOperatorInput { Code = $"S{_stage.Marker}", Name = "Op" });
        var location = (await admin.GetLocationsAsync())[0];

        var result = await admin.SaveRouteAsync(new AdminRouteInput
        {
            OperatorId = op.Id!.Value,
            OriginLocationId = location.Id,
            DestinationLocationId = location.Id,
            Name = "Vòng tròn",
            DistanceKm = 10,
            EstimatedDurationMinutes = 10
        });

        Assert.False(result.Success);
    }

    public static IEnumerable<object[]> InvalidStops()
    {
        AdminRouteStopDto Stop(long id, int order, bool pickup, bool dropoff, int? departure = null) =>
            new() { PointId = id, StopOrder = order, AllowPickup = pickup, AllowDropoff = dropoff, DepartureOffsetMinutes = departure };

        yield return new object[] { new List<AdminRouteStopDto> { Stop(1, 1, true, true) } };
        yield return new object[] { new List<AdminRouteStopDto> { Stop(1, 1, true, false), Stop(2, 1, false, true) } };
        yield return new object[] { new List<AdminRouteStopDto> { Stop(1, 1, true, false), Stop(1, 2, false, true) } };
        yield return new object[] { new List<AdminRouteStopDto> { Stop(1, 1, true, false), Stop(2, 2, true, false) } };
        yield return new object[] { new List<AdminRouteStopDto> { Stop(1, 1, false, true), Stop(2, 2, true, false) } };
        yield return new object[] { new List<AdminRouteStopDto> { Stop(1, 1, false, false), Stop(2, 2, false, true) } };
        yield return new object[] { new List<AdminRouteStopDto> { Stop(1, 1, true, false, 60), Stop(2, 2, false, true, 30) } };
    }

    [Theory]
    [MemberData(nameof(InvalidStops))]
    internal void InvalidStopSequences_AreRejected(List<AdminRouteStopDto> stops)
    {
        Assert.NotNull(AdminService.ValidateStops(stops));
    }

    // ---------------------------------------------------------------- fleet

    [Fact]
    public async Task Bus_RequiresADeclaredSeatLayout_AndNeverInventsOne()
    {
        var (context, admin, _) = await AdminAsync();
        await using var _ = context;
        var op = await admin.SaveOperatorAsync(new AdminOperatorInput { Code = $"B{_stage.Marker}", Name = "Op" });
        var busTypeId = await context.BusTypes.Select(t => t.Id).FirstAsync();

        foreach (var layout in new[] { "", "A01,1,1", "A01,1,1,1,Seat\nA01,1,1,2,Seat", "A01,1,1,1,Seat\nA02,1,1,1,Seat" })
        {
            var result = await admin.CreateBusAsync(new CreateBusDto
            {
                OperatorId = op.Id!.Value,
                BusTypeId = busTypeId,
                LicensePlate = $"N{_stage.Marker}",
                SeatLayoutDefinition = layout
            });
            Assert.False(result.Success, $"Layout should be rejected: '{layout}'");
        }
    }

    [Fact]
    public async Task DuplicateLicensePlate_IsRejected()
    {
        var (context, admin, _) = await AdminAsync();
        await using var _ = context;
        var (operatorId, _, _) = await NetworkAsync(admin, context);
        var busTypeId = await context.BusTypes.Select(t => t.Id).FirstAsync();

        var duplicate = await admin.CreateBusAsync(new CreateBusDto
        {
            OperatorId = operatorId,
            BusTypeId = busTypeId,
            LicensePlate = $"PA{_stage.Marker}",
            SeatLayoutDefinition = "A01,1,1,1,Seat"
        });

        Assert.False(duplicate.Success);
    }

    [Fact]
    public async Task BusLayout_CannotChange_WhileTripsAreScheduledOnIt()
    {
        var (context, admin, _) = await AdminAsync();
        await using var _ = context;
        var (_, routeId, busId) = await NetworkAsync(admin, context);
        Assert.True((await admin.SaveTripAsync(TripInput(routeId, busId))).Success);
        await MarkTripCodesForCleanupAsync(context, routeId);

        var result = await admin.ReplaceBusLayoutAsync(busId, "Z01,1,1,1,Seat");

        Assert.False(result.Success);
    }

    // ---------------------------------------------------------------- trips

    [Fact]
    public async Task ScheduleConflict_OnTheSameBus_IsRejected()
    {
        var (context, admin, _) = await AdminAsync();
        await using var _ = context;
        var (_, routeId, busId) = await NetworkAsync(admin, context);
        var first = TripInput(routeId, busId);
        Assert.True((await admin.SaveTripAsync(first)).Success);
        await MarkTripCodesForCleanupAsync(context, routeId);

        var overlapping = TripInput(routeId, busId);
        overlapping.DepartureTime = first.DepartureTime.AddMinutes(30);
        overlapping.ExpectedArrivalTime = first.ExpectedArrivalTime.AddMinutes(30);

        var result = await admin.SaveTripAsync(overlapping);

        Assert.False(result.Success);
        Assert.Contains("trùng thời gian", result.Message);
    }

    [Fact]
    public async Task Trip_RejectsBusFromAnotherOperator()
    {
        var (context, admin, _) = await AdminAsync();
        await using var _ = context;
        var (_, routeId, _) = await NetworkAsync(admin, context, "A");
        var (_, _, otherBusId) = await NetworkAsync(admin, context, "B");

        var result = await admin.SaveTripAsync(TripInput(routeId, otherBusId));

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Trip_RejectsInvalidTimesAndPrices()
    {
        var (context, admin, _) = await AdminAsync();
        await using var _ = context;
        var (_, routeId, busId) = await NetworkAsync(admin, context);

        var past = TripInput(routeId, busId);
        past.DepartureTime = DateTimeOffset.UtcNow.AddDays(-1);
        past.ExpectedArrivalTime = past.DepartureTime.AddHours(1);
        Assert.False((await admin.SaveTripAsync(past)).Success);

        var backwards = TripInput(routeId, busId);
        backwards.ExpectedArrivalTime = backwards.DepartureTime.AddHours(-1);
        Assert.False((await admin.SaveTripAsync(backwards)).Success);

        Assert.False((await admin.SaveTripAsync(TripInput(routeId, busId, price: 0))).Success);
    }

    [Fact]
    public async Task Driver_MustBelongToTheOperator_HaveAValidLicense_AndBeFree()
    {
        var (context, admin, _) = await AdminAsync();
        await using var _ = context;
        var (operatorId, routeId, busId) = await NetworkAsync(admin, context);

        var expired = await admin.SaveDriverAsync(new AdminDriverInput
        {
            OperatorId = operatorId,
            FullName = "Tài xế Hết Hạn",
            Phone = "0900000011",
            LicenseNumber = $"L1{_stage.Marker}",
            LicenseExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
        });
        var expiredTrip = TripInput(routeId, busId);
        expiredTrip.DriverIds.Add(expired.Id!.Value);
        Assert.False((await admin.SaveTripAsync(expiredTrip)).Success);

        var (otherOperatorId, _, _) = await NetworkAsync(admin, context, "B");
        var foreign = await admin.SaveDriverAsync(new AdminDriverInput
        {
            OperatorId = otherOperatorId,
            FullName = "Tài xế Khác",
            Phone = "0900000012",
            LicenseNumber = $"L2{_stage.Marker}"
        });
        var foreignTrip = TripInput(routeId, busId);
        foreignTrip.DriverIds.Add(foreign.Id!.Value);
        Assert.False((await admin.SaveTripAsync(foreignTrip)).Success);

        var free = await admin.SaveDriverAsync(new AdminDriverInput
        {
            OperatorId = operatorId,
            FullName = "Tài xế Rảnh",
            Phone = "0900000013",
            LicenseNumber = $"L3{_stage.Marker}"
        });
        var firstTrip = TripInput(routeId, busId);
        firstTrip.DriverIds.Add(free.Id!.Value);
        Assert.True((await admin.SaveTripAsync(firstTrip)).Success);
        await MarkTripCodesForCleanupAsync(context, routeId);

        var busTypeId = await context.BusTypes.Select(t => t.Id).FirstAsync();
        var secondBus = await admin.CreateBusAsync(new CreateBusDto
        {
            OperatorId = operatorId,
            BusTypeId = busTypeId,
            LicensePlate = $"P2{_stage.Marker}",
            SeatLayoutDefinition = "A01,1,1,1,Seat"
        });
        var conflictingTrip = TripInput(routeId, secondBus.Id!.Value);
        conflictingTrip.DepartureTime = firstTrip.DepartureTime.AddMinutes(10);
        conflictingTrip.ExpectedArrivalTime = firstTrip.ExpectedArrivalTime.AddMinutes(10);
        conflictingTrip.DriverIds.Add(free.Id.Value);

        var conflict = await admin.SaveTripAsync(conflictingTrip);

        Assert.False(conflict.Success);
        Assert.Contains("trùng thời gian", conflict.Message);
    }

    [Fact]
    public async Task ReferenceTrip_IsNotBookable_UntilExplicitlyOpenedAsSandbox()
    {
        var (context, admin, _) = await AdminAsync();
        await using var _ = context;
        var (_, routeId, busId) = await NetworkAsync(admin, context);

        var created = await admin.SaveTripAsync(TripInput(routeId, busId));
        await MarkTripCodesForCleanupAsync(context, routeId);

        var stored = await context.Trips.AsNoTracking().SingleAsync(t => t.Id == created.Id);
        Assert.False(stored.IsDemoTrip);
        Assert.Equal(TransportDataSource.ReferenceSchedule, stored.DataSource);
        var hold = await _stage.Bookings(context).HoldSeatsAsync(new SeatHoldRequestDto { TripId = stored.Id, SeatCodes = new() { "A01" } });
        Assert.False(hold.Success);
    }

    [Fact]
    public async Task SandboxTrip_RequiresRouteStops()
    {
        var (context, admin, _) = await AdminAsync();
        await using var _ = context;
        var (_, routeId, busId) = await NetworkAsync(admin, context);

        var input = TripInput(routeId, busId);
        input.OpenAsSandbox = true;

        Assert.False((await admin.SaveTripAsync(input)).Success);
    }

    [Fact]
    public async Task TripStatus_FollowsItsLifecycle_AndCannotJump()
    {
        await using var context = Stage.NewContext();
        var adminId = await _stage.CreateUserAsync(context, RoleNames.Admin);
        var admin = _stage.Admin(context, adminId);
        var trip = await _stage.CreateTripAsync(context);

        Assert.False((await admin.SetTripStatusAsync(trip.Id, nameof(TripStatus.Completed))).Success);
        Assert.False((await admin.SetTripStatusAsync(trip.Id, nameof(TripStatus.Cancelled))).Success);
        Assert.True((await admin.SetTripStatusAsync(trip.Id, nameof(TripStatus.Boarding))).Success);
        Assert.False((await admin.SetTripStatusAsync(trip.Id, nameof(TripStatus.Scheduled))).Success);
    }

    [Fact]
    public async Task CancellingATrip_ReleasesUnpaid_RefundsPaid_AndRevokesTickets()
    {
        await using var context = Stage.NewContext();
        var adminId = await _stage.CreateUserAsync(context, RoleNames.Admin);
        var (trip, pending, _) = await _stage.CreatePendingBookingAsync(context);

        // A second customer on the same trip, who pays and opens their ticket.
        var payerOwner = Guid.NewGuid().ToString("N");
        var payerBookings = _stage.Bookings(context, payerOwner);
        var free = (await payerBookings.GetSeatsForTripAsync(trip.Id)).First(s => s.IsAvailable);
        var hold = await payerBookings.HoldSeatsAsync(new SeatHoldRequestDto { TripId = trip.Id, SeatCodes = new() { free.SeatCode } });
        var created = await payerBookings.CreateBookingAsync(new CreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = hold.HoldToken!,
            SeatCodes = new() { free.SeatCode },
            PassengerName = "Khach Da Tra",
            PassengerPhone = "0912345679",
            PassengerEmail = "paid@tripgo.test",
            ContactEmailConfirmed = true
        });
        Assert.True(created.Success, created.Message);
        Assert.True((await _stage.Payments(context, payerOwner).ProcessPaymentAsync(new ProcessPaymentRequestDto
        {
            BookingCode = created.BookingCode!,
            Amount = created.TotalAmount,
            SimulateSuccess = true
        })).Success);
        var ticket = await _stage.Tickets(context, payerOwner).GetMyTicketAsync(created.BookingCode!);
        Assert.True(ticket.Available);

        var result = await _stage.Admin(context, adminId).CancelTripAsync(trip.Id, "Xe hỏng không thể khởi hành");

        Assert.True(result.Success, result.Message);
        var unpaid = await context.Bookings.AsNoTracking().SingleAsync(b => b.Id == pending.Id);
        Assert.Equal(BookingStatus.Cancelled, unpaid.Status);
        var paid = await context.Bookings.AsNoTracking().Include(b => b.PaymentTransactions).SingleAsync(b => b.BookingCode == created.BookingCode);
        Assert.Equal(BookingStatus.Cancelled, paid.Status);
        Assert.Equal(PaymentStatus.Refunded, paid.PaymentStatus);
        Assert.NotNull(paid.PaymentTransactions.Single(t => t.Status == PaymentStatus.Refunded).RefundedAt);
        Assert.NotNull(await context.TicketCredentials.Where(c => c.BookingId == paid.Id).Select(c => c.RevokedAt).SingleAsync());
        Assert.Equal(0, await context.TripSeats.CountAsync(s => s.TripId == trip.Id && s.Status != TripSeatStatus.Available));
    }

    // ---------------------------------------------------------------- bookings

    [Fact]
    public async Task BookingList_MasksPassengerData_AndRevealIsAudited()
    {
        await using var context = Stage.NewContext();
        var adminId = await _stage.CreateUserAsync(context, RoleNames.Admin);
        var admin = _stage.Admin(context, adminId);
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);

        var page = await admin.GetBookingsAsync(new AdminBookingFilter { Search = booking.BookingCode });
        var row = Assert.Single(page.Items);
        Assert.DoesNotContain("Nguyen Van Khach", row.MaskedPassengerName);
        Assert.DoesNotContain("0912345678", row.MaskedPhone);

        var masked = await admin.GetBookingAsync(booking.Id);
        Assert.Null(masked!.PassengerPhone);
        Assert.False(await context.AdminAuditLogs.AnyAsync(a => a.Action == "Booking.RevealContact" && a.EntityId == booking.Id.ToString()));

        var revealed = await admin.GetBookingAsync(booking.Id, revealContact: true);
        Assert.Equal("0912345678", revealed!.PassengerPhone);
        Assert.True(await context.AdminAuditLogs.AnyAsync(a => a.Action == "Booking.RevealContact" && a.EntityId == booking.Id.ToString()));
    }

    [Fact]
    public async Task BookingList_Paginates_AndFilters()
    {
        await using var context = Stage.NewContext();
        var adminId = await _stage.CreateUserAsync(context, RoleNames.Admin);
        var admin = _stage.Admin(context, adminId);
        await _stage.CreatePendingBookingAsync(context);
        await _stage.CreatePendingBookingAsync(context);

        var firstPage = await admin.GetBookingsAsync(new AdminBookingFilter { Status = nameof(BookingStatus.PendingPayment), PageSize = 5, Page = 1 });

        Assert.All(firstPage.Items, row => Assert.Equal(nameof(BookingStatus.PendingPayment), row.Status));
        Assert.True(firstPage.TotalCount >= 2);
        Assert.True(firstPage.Items.Count <= 5);
        Assert.Empty((await admin.GetBookingsAsync(new AdminBookingFilter { Search = "no-such-booking-" + _stage.Marker })).Items);
    }

    [Fact]
    public async Task AdminCancel_OfAPaidBooking_GoesThroughARecordedRefund()
    {
        await using var context = Stage.NewContext();
        var adminId = await _stage.CreateUserAsync(context, RoleNames.Admin);
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);
        await _stage.Payments(context).ProcessPaymentAsync(new ProcessPaymentRequestDto { BookingCode = booking.BookingCode, Amount = booking.TotalAmount, SimulateSuccess = true });

        var result = await _stage.Admin(context, adminId).CancelBookingAsync(booking.Id, "Khách yêu cầu hủy qua tổng đài");

        Assert.True(result.Success, result.Message);
        var stored = await context.Bookings.AsNoTracking().Include(b => b.PaymentTransactions).SingleAsync(b => b.Id == booking.Id);
        Assert.Equal(PaymentStatus.Refunded, stored.PaymentStatus);
        Assert.NotNull(stored.PaymentTransactions.Single().RefundReference);
        Assert.True(await context.AdminAuditLogs.AnyAsync(a => a.Action == "Booking.Cancel" && a.EntityId == booking.Id.ToString()));
    }

    [Fact]
    public async Task AdminCancel_RequiresAReason()
    {
        await using var context = Stage.NewContext();
        var adminId = await _stage.CreateUserAsync(context, RoleNames.Admin);
        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);

        Assert.False((await _stage.Admin(context, adminId).CancelBookingAsync(booking.Id, "  ")).Success);
    }

    [Fact]
    public async Task Dashboard_CountsActualRecords_AndSeparatesSandboxFromRealMoney()
    {
        await using var context = Stage.NewContext();
        var adminId = await _stage.CreateUserAsync(context, RoleNames.Admin);
        var admin = _stage.Admin(context, adminId);
        var before = await admin.GetDashboardAsync();

        var (_, booking, _) = await _stage.CreatePendingBookingAsync(context);
        var afterCreate = await admin.GetDashboardAsync();
        Assert.Equal(before.TotalBookings + 1, afterCreate.TotalBookings);
        Assert.Equal(before.PendingBookings + 1, afterCreate.PendingBookings);

        await _stage.Payments(context).ProcessPaymentAsync(new ProcessPaymentRequestDto { BookingCode = booking.BookingCode, Amount = booking.TotalAmount, SimulateSuccess = true });
        var afterPay = await admin.GetDashboardAsync();

        Assert.Equal(before.SandboxPaidTransactions + 1, afterPay.SandboxPaidTransactions);
        Assert.Equal((before.SandboxPaidAmount ?? 0) + booking.TotalAmount, afterPay.SandboxPaidAmount);
        Assert.Equal(before.RealPaidAmount, afterPay.RealPaidAmount);
    }
}
