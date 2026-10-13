using System;
using System.Collections.Generic;
using Tripgo.Application.DTOs;
using Tripgo.Application.Services;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;
using Xunit;

namespace Tripgo.Tests;

public class TripSearchResultDtoTests
{
    [Fact]
    public void TripSearchResultDto_InitializesWithEmptyStops()
    {
        var dto = new TripSearchResultDto();

        Assert.NotNull(dto.PickupPoints);
        Assert.NotNull(dto.DropoffPoints);
        Assert.Empty(dto.PickupPoints);
        Assert.Empty(dto.DropoffPoints);
    }

    [Fact]
    public void TripSearchResultDto_CanHoldVerifiedStops()
    {
        var dto = new TripSearchResultDto
        {
            Id = 1,
            TripCode = "TRONGMINH-SGN-DLI-01",
            PickupPoints =
            {
                new PickupDropoffPointDto
                {
                    Id = 10,
                    Name = "Văn phòng Lê Hồng Phong",
                    Address = "147 Lê Hồng Phong, P.3, Q.5, TP.HCM",
                    Time = "22:00",
                    IsPickup = true
                }
            },
            DropoffPoints =
            {
                new PickupDropoffPointDto
                {
                    Id = 20,
                    Name = "Bến xe Đa Thiện",
                    Address = "01 Mai Anh Đào, P.8, TP. Đà Lạt",
                    Time = "05:30",
                    IsPickup = false
                }
            }
        };

        Assert.Single(dto.PickupPoints);
        Assert.Equal("Văn phòng Lê Hồng Phong", dto.PickupPoints[0].Name);
        Assert.Equal("22:00", dto.PickupPoints[0].Time);
        Assert.Single(dto.DropoffPoints);
        Assert.Equal("Bến xe Đa Thiện", dto.DropoffPoints[0].Name);
        Assert.Equal("05:30", dto.DropoffPoints[0].Time);
    }

    [Fact]
    public void TripSearchResultDto_DefaultsToReferenceScheduleAndNonBookable()
    {
        var dto = new TripSearchResultDto();

        Assert.False(dto.IsBookable);
        Assert.Equal("PUBLISHED_REFERENCE_SCHEDULE", dto.OperationalTier);
        Assert.Equal("Lịch trình tham khảo", dto.OperationalTierLabel);
    }
}

public class VerifiedStopMappingTests
{
    [Fact]
    public void ExtractTripStops_WhenNoStopsInDatabase_ReturnsEmptyListsWithoutFabrication()
    {
        // Arrange
        var trip = new Trip
        {
            Id = 100,
            TripCode = "TEST-TRIP-01",
            DepartureTime = DateTimeOffset.Now,
            ExpectedArrivalTime = DateTimeOffset.Now.AddHours(7),
            Route = new Route
            {
                Id = 1,
                Name = "Sài Gòn - Đà Lạt",
                Stops = new List<RouteStop>()
            },
            Stops = new List<TripStop>()
        };

        // Act
        var (pickups, dropoffs) = TripService.ExtractTripStops(trip);

        // Assert - strictly zero fabricated fallback stops
        Assert.NotNull(pickups);
        Assert.NotNull(dropoffs);
        Assert.Empty(pickups);
        Assert.Empty(dropoffs);
    }

    [Fact]
    public void ExtractTripStops_FromTripStops_MapsVerifiedPickupAndDropoffCorrectly()
    {
        // Arrange
        var depTime = new DateTimeOffset(2026, 10, 10, 22, 0, 0, TimeSpan.FromHours(7));
        var arrTime = depTime.AddHours(6).AddMinutes(30);

        var pickupPoint = new PickupDropoffPoint
        {
            Id = 501,
            Name = "Bến xe Miền Tây",
            Address = "395 Kinh Dương Vương, Bình Tân, TP.HCM",
            PointType = PointType.Pickup
        };

        var dropoffPoint = new PickupDropoffPoint
        {
            Id = 502,
            Name = "Bến xe Đa Thiện",
            Address = "01 Mai Anh Đào, P.8, TP. Đà Lạt",
            PointType = PointType.Dropoff
        };

        var trip = new Trip
        {
            Id = 101,
            TripCode = "TRONGMINH-SGN-DLI-2200",
            DepartureTime = depTime,
            ExpectedArrivalTime = arrTime,
            Stops = new List<TripStop>
            {
                new TripStop
                {
                    Id = 1,
                    PointId = 501,
                    Point = pickupPoint,
                    StopOrder = 1,
                    ScheduledDepartureTime = depTime,
                    AllowPickup = true,
                    AllowDropoff = false
                },
                new TripStop
                {
                    Id = 2,
                    PointId = 502,
                    Point = dropoffPoint,
                    StopOrder = 2,
                    ScheduledArrivalTime = arrTime,
                    AllowPickup = false,
                    AllowDropoff = true
                }
            }
        };

        // Act
        var (pickups, dropoffs) = TripService.ExtractTripStops(trip);

        // Assert
        Assert.Single(pickups);
        Assert.Equal("Bến xe Miền Tây", pickups[0].Name);
        Assert.Equal("395 Kinh Dương Vương, Bình Tân, TP.HCM", pickups[0].Address);
        Assert.True(pickups[0].IsPickup);

        Assert.Single(dropoffs);
        Assert.Equal("Bến xe Đa Thiện", dropoffs[0].Name);
        Assert.Equal("01 Mai Anh Đào, P.8, TP. Đà Lạt", dropoffs[0].Address);
        Assert.False(dropoffs[0].IsPickup);
    }

    [Fact]
    public void ExtractTripStops_FromRouteStops_AppliesOffsetsCorrectly()
    {
        // Arrange
        var depTime = new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.FromHours(7));
        var arrTime = depTime.AddHours(7);

        var pickupPt = new PickupDropoffPoint
        {
            Id = 601,
            Name = "Văn phòng Lê Hồng Phong",
            Address = "233 Lê Hồng Phong, Q.5, TP.HCM",
            PointType = PointType.Pickup
        };

        var dropoffPt = new PickupDropoffPoint
        {
            Id = 602,
            Name = "Bến xe Liên tỉnh Đà Lạt",
            Address = "01 Tô Hiến Thành, P.3, TP. Đà Lạt",
            PointType = PointType.Dropoff
        };

        var trip = new Trip
        {
            Id = 102,
            TripCode = "FUTA-SGN-DLI-0800",
            DepartureTime = depTime,
            ExpectedArrivalTime = arrTime,
            Stops = new List<TripStop>(), // no trip-specific overrides
            Route = new Route
            {
                Id = 2,
                Name = "Sài Gòn - Đà Lạt",
                Stops = new List<RouteStop>
                {
                    new RouteStop
                    {
                        Id = 11,
                        PointId = 601,
                        Point = pickupPt,
                        StopOrder = 1,
                        DepartureOffsetMinutes = 30, // 08:30
                        AllowPickup = true,
                        AllowDropoff = false
                    },
                    new RouteStop
                    {
                        Id = 12,
                        PointId = 602,
                        Point = dropoffPt,
                        StopOrder = 2,
                        ArrivalOffsetMinutes = 420, // 08:00 + 7h = 15:00
                        AllowPickup = false,
                        AllowDropoff = true
                    }
                }
            }
        };

        // Act
        var (pickups, dropoffs) = TripService.ExtractTripStops(trip);

        // Assert
        Assert.Single(pickups);
        Assert.Equal("Văn phòng Lê Hồng Phong", pickups[0].Name);
        Assert.Equal(depTime.AddMinutes(30).ToLocalTime().ToString("HH:mm"), pickups[0].Time);
        Assert.True(pickups[0].HasFixedTime);

        Assert.Single(dropoffs);
        Assert.Equal("Bến xe Liên tỉnh Đà Lạt", dropoffs[0].Name);
        Assert.Equal(depTime.AddMinutes(420).ToLocalTime().ToString("HH:mm"), dropoffs[0].Time);
        Assert.True(dropoffs[0].HasFixedTime);
    }

    [Fact]
    public void ExtractTripStops_NullDepartureOffset_ProducesUnknownPickupTime()
    {
        // Arrange: RouteStop with DepartureOffsetMinutes = NULL (unverified intermediate stop)
        var depTime = new DateTimeOffset(2026, 10, 10, 11, 0, 0, TimeSpan.FromHours(7));
        var trip = new Trip
        {
            Id = 201,
            TripCode = "TRONGMINH-SGN-DLI-1100",
            DepartureTime = depTime,
            ExpectedArrivalTime = depTime.AddHours(7),
            Route = new Route
            {
                Id = 1,
                Name = "Sài Gòn - Đà Lạt",
                Stops = new List<RouteStop>
                {
                    new RouteStop
                    {
                        Id = 1,
                        PointId = 101,
                        Point = new PickupDropoffPoint
                        {
                            Id = 101,
                            Name = "Văn phòng Quận 5",
                            Address = "147 Lê Hồng Phong, Q.5, TP.HCM",
                            PointType = PointType.Pickup
                        },
                        StopOrder = 2,
                        DepartureOffsetMinutes = null, // UNVERIFIED / UNKNOWN OFFSET
                        AllowPickup = true,
                        AllowDropoff = false
                    }
                }
            }
        };

        // Act
        var (pickups, _) = TripService.ExtractTripStops(trip);

        // Assert
        Assert.Single(pickups);
        var pickup = pickups[0];
        Assert.Equal(PickupDropoffPointDto.UnknownTimeText, pickup.Time);
        Assert.False(pickup.HasFixedTime);
        Assert.NotEqual(depTime.ToLocalTime().ToString("HH:mm"), pickup.Time); // Must NOT fabricate origin departure time
        Assert.NotEqual("00:00", pickup.Time); // Must NOT default to midnight
    }

    [Fact]
    public void ExtractTripStops_NullArrivalOffset_ProducesUnknownDropoffTime()
    {
        // Arrange: RouteStop with ArrivalOffsetMinutes = NULL (unverified intermediate stop)
        var depTime = new DateTimeOffset(2026, 10, 10, 11, 0, 0, TimeSpan.FromHours(7));
        var arrTime = depTime.AddHours(7);
        var trip = new Trip
        {
            Id = 202,
            TripCode = "TRONGMINH-SGN-DLI-1100",
            DepartureTime = depTime,
            ExpectedArrivalTime = arrTime,
            Route = new Route
            {
                Id = 1,
                Name = "Sài Gòn - Đà Lạt",
                Stops = new List<RouteStop>
                {
                    new RouteStop
                    {
                        Id = 2,
                        PointId = 102,
                        Point = new PickupDropoffPoint
                        {
                            Id = 102,
                            Name = "Trạm Đức Trọng (Phi Nôm)",
                            Address = "Ngã 3 Phi Nôm, Đức Trọng, Lâm Đồng",
                            PointType = PointType.Dropoff
                        },
                        StopOrder = 4,
                        ArrivalOffsetMinutes = null, // UNVERIFIED / UNKNOWN OFFSET
                        AllowPickup = false,
                        AllowDropoff = true
                    }
                }
            }
        };

        // Act
        var (_, dropoffs) = TripService.ExtractTripStops(trip);

        // Assert
        Assert.Single(dropoffs);
        var dropoff = dropoffs[0];
        Assert.Equal(PickupDropoffPointDto.UnknownTimeText, dropoff.Time);
        Assert.False(dropoff.HasFixedTime);
        Assert.NotEqual(arrTime.ToLocalTime().ToString("HH:mm"), dropoff.Time); // Must NOT fabricate terminus arrival time
        Assert.NotEqual("00:00", dropoff.Time); // Must NOT default to midnight
    }

    [Fact]
    public void ExtractTripStops_ValidOffsets_ProduceCorrectLocalTime_AndDistinctFromMidnight()
    {
        // Arrange: Midnight departure with offset 0 vs unknown offset
        var midnightDep = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.FromHours(7));
        var trip = new Trip
        {
            Id = 203,
            TripCode = "MIDNIGHT-TRIP-0000",
            DepartureTime = midnightDep,
            ExpectedArrivalTime = midnightDep.AddHours(6),
            Route = new Route
            {
                Id = 1,
                Name = "Tuyến Đêm",
                Stops = new List<RouteStop>
                {
                    // Verified origin departure at midnight (00:00)
                    new RouteStop
                    {
                        Id = 1,
                        PointId = 201,
                        Point = new PickupDropoffPoint { Id = 201, Name = "Bến xe xuất phát", Address = "Hà Nội" },
                        StopOrder = 1,
                        DepartureOffsetMinutes = 0,
                        AllowPickup = true
                    },
                    // Intermediate stop with unknown offset
                    new RouteStop
                    {
                        Id = 2,
                        PointId = 202,
                        Point = new PickupDropoffPoint { Id = 202, Name = "Trạm đón trung gian", Address = "Nội Bài" },
                        StopOrder = 2,
                        DepartureOffsetMinutes = null,
                        AllowPickup = true
                    }
                }
            }
        };

        // Act
        var (pickups, _) = TripService.ExtractTripStops(trip);

        // Assert
        Assert.Equal(2, pickups.Count);

        // Origin at midnight: verified exact time "00:00"
        Assert.Equal("00:00", pickups[0].Time);
        Assert.True(pickups[0].HasFixedTime);

        // Intermediate unknown stop: explicit dynamic dispatch text, NOT "00:00"
        Assert.Equal(PickupDropoffPointDto.UnknownTimeText, pickups[1].Time);
        Assert.False(pickups[1].HasFixedTime);
        Assert.NotEqual("00:00", pickups[1].Time);
    }

    [Fact]
    public void SearchTrips_PresentationContract_RendersUnknownTimeWithoutMisleadingTimestamps()
    {
        // Arrange: A realistic 3-stop corridor:
        // Stop 1 (Origin): Offset 0 (verified 13:00)
        // Stop 2 (Intermediate): Offset NULL (unverified)
        // Stop 3 (Terminus): Offset 480 (verified 21:00)
        var depTime = new DateTimeOffset(2026, 10, 10, 13, 0, 0, TimeSpan.FromHours(7));
        var trip = new Trip
        {
            Id = 204,
            TripCode = "XENHA-SGN-NHA-1300",
            DepartureTime = depTime,
            ExpectedArrivalTime = depTime.AddMinutes(480),
            Route = new Route
            {
                Id = 3,
                Name = "Sài Gòn - Nha Trang",
                Stops = new List<RouteStop>
                {
                    new RouteStop
                    {
                        Id = 1,
                        PointId = 301,
                        Point = new PickupDropoffPoint { Id = 301, Name = "VP Phạm Ngũ Lão", Address = "Q.1, TP.HCM" },
                        StopOrder = 1,
                        DepartureOffsetMinutes = 0,
                        AllowPickup = true
                    },
                    new RouteStop
                    {
                        Id = 2,
                        PointId = 302,
                        Point = new PickupDropoffPoint { Id = 302, Name = "Trạm Hàng Xanh", Address = "Bình Thạnh, TP.HCM" },
                        StopOrder = 2,
                        DepartureOffsetMinutes = null, // Unknown
                        AllowPickup = true
                    },
                    new RouteStop
                    {
                        Id = 3,
                        PointId = 303,
                        Point = new PickupDropoffPoint { Id = 303, Name = "VP Hoàng Hoa Thám", Address = "Nha Trang" },
                        StopOrder = 3,
                        ArrivalOffsetMinutes = 480, // Verified terminus arrival
                        AllowDropoff = true
                    }
                }
            }
        };

        // Act
        var (pickups, dropoffs) = TripService.ExtractTripStops(trip);

        // Assert
        Assert.Equal(2, pickups.Count);
        Assert.Single(dropoffs);

        // Origin stop: exact timestamp
        Assert.Equal("13:00", pickups[0].Time);
        Assert.True(pickups[0].HasFixedTime);

        // Intermediate pickup stop: unknown dynamic dispatch text, NOT origin 13:00
        Assert.Equal("Giờ đón/trả theo điều phối", pickups[1].Time);
        Assert.False(pickups[1].HasFixedTime);
        Assert.NotEqual("13:00", pickups[1].Time);

        // Terminus dropoff stop: exact arrival timestamp
        Assert.Equal("21:00", dropoffs[0].Time);
        Assert.True(dropoffs[0].HasFixedTime);
    }
}

public class ReferenceTripBookingRestrictionTests
{
    [Fact]
    public void ReferenceTrips_DefaultToNonBookableAndPublishedReferenceTier()
    {
        var searchDto = new TripSearchResultDto();
        var detailDto = new TripDetailDto();

        Assert.False(searchDto.IsBookable);
        Assert.Equal("PUBLISHED_REFERENCE_SCHEDULE", searchDto.OperationalTier);
        Assert.Equal("Lịch trình tham khảo", searchDto.OperationalTierLabel);

        Assert.False(detailDto.IsBookable);
        Assert.Equal("PUBLISHED_REFERENCE_SCHEDULE", detailDto.OperationalTier);
        Assert.Equal("Lịch trình tham khảo", detailDto.OperationalTierLabel);
    }

    [Fact]
    public async Task BookSeatsAsync_ForReferenceTrip_StrictlyRejectsBooking()
    {
        // Arrange
        var dummyRepo = new DummyTripRepository();
        var dummyStore = new DummyBookingStore();
        var tripService = new TripService(dummyRepo, dummyStore);

        var customer = new CustomerBookingInfoDto
        {
            Name = "Nguyen Van A",
            Phone = "0901234567"
        };

        // Act: Attempt to book seats on reference trip
        var result = await tripService.BookSeatsAsync(999, new List<string> { "A01" }, customer);

        // Assert: Booking must be strictly rejected
        Assert.False(result);
    }

    private class DummyTripRepository : Tripgo.Application.Interfaces.Persistence.ITripRepository
    {
        public Task<List<Location>> GetActiveLocationsAsync(System.Threading.CancellationToken ct = default) => Task.FromResult(new List<Location>());
        public Task<List<Operator>> GetActiveOperatorsAsync(System.Threading.CancellationToken ct = default) => Task.FromResult(new List<Operator>());
        public Task<bool> OperatorExistsAndActiveAsync(long id, System.Threading.CancellationToken ct = default) => Task.FromResult(true);
        public Task AddOperatorAsync(Operator operatorEntity, System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<BusType>> GetActiveBusTypesAsync(System.Threading.CancellationToken ct = default) => Task.FromResult(new List<BusType>());
        public Task<BusType?> GetActiveBusTypeByIdAsync(long id, System.Threading.CancellationToken ct = default) => Task.FromResult<BusType?>(null);
        public Task<List<Bus>> GetActiveBusesByOperatorAsync(long operatorId, System.Threading.CancellationToken ct = default) => Task.FromResult(new List<Bus>());
        public Task<bool> BusExistsAndActiveAsync(long id, System.Threading.CancellationToken ct = default) => Task.FromResult(true);
        public Task<long?> GetBusOperatorIdAsync(long busId, System.Threading.CancellationToken ct = default) => Task.FromResult<long?>(null);
        public Task AddBusAsync(Bus bus, System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<Route>> GetActiveRoutesAsync(System.Threading.CancellationToken ct = default) => Task.FromResult(new List<Route>());
        public Task<long?> GetRouteOperatorIdAsync(long routeId, System.Threading.CancellationToken ct = default) => Task.FromResult<long?>(null);
        public Task AddRouteAsync(Route route, System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<Trip>> SearchTripsAsync(long originLocationId, long destinationLocationId, DateTimeOffset windowStart, DateTimeOffset windowEnd, System.Threading.CancellationToken ct = default) => Task.FromResult(new List<Trip>());
        public Task<Trip?> GetTripWithDetailsAsync(long tripId, System.Threading.CancellationToken ct = default) => Task.FromResult<Trip?>(null);
        public Task<List<Trip>> GetAllTripsAdminAsync(System.Threading.CancellationToken ct = default) => Task.FromResult(new List<Trip>());
        public Task<Trip?> GetTripForBookingAsync(long tripId, System.Threading.CancellationToken ct = default) => Task.FromResult<Trip?>(null);
        public Task AddTripAsync(Trip trip, System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> CancelTripAsync(long tripId, System.Threading.CancellationToken ct = default) => Task.FromResult(true);
    }

    private class DummyBookingStore : Tripgo.Application.Services.Temporary.ITemporaryBookingStore
    {
        public int GetBookedSeatCount(long tripId) => 0;
        public HashSet<string> GetBookedSeatSnapshot(long tripId) => new();
        public bool TryBookSeats(long tripId, List<string> internalSeatCodes, List<string> requestedCodes, CustomerBookingInfoDto customerInfo, string routeName, string operatorName, string busTypeName, string busLicensePlate, DateTimeOffset departureTime, decimal basePrice) => false;
        public List<CustomerBookingInfoDto> GetBookingsByPhone(string normalizedPhone) => new();
        public CustomerBookingInfoDto? GetBookingByCode(string bookingCode) => null;
        public void EnqueueSupportCallback(SupportCallbackRequestDto callback) { }
        public void RemoveTripBookedSeats(long tripId) { }
    }
}