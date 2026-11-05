using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Tripgo.Application.DTOs;
using Tripgo.Application.Interfaces;
using Tripgo.Application.Interfaces.Persistence;
using Tripgo.Application.Services;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;
using Tripgo.Infrastructure.Persistence;
using Tripgo.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Tripgo.Tests;

public class TripSearchResultDtoTests
{
    [Theory]
    [InlineData("TripgoDb")]
    [InlineData("TripgoDb_Unexpected")]
    public async Task AppDbContext_RejectsTrackedWritesOutsideIsolatedTestDatabase(string databaseName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(
                $"Server=localhost\\MSSQLSERVER01;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;

        await using var context = new AppDbContext(options);
        context.Roles.Add(new Role { Name = "ReadOnlyGuardTest" });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.SaveChangesAsync());

        Assert.Contains("Writes are permitted only against TripgoDb_IsolatedTest", error.Message);
    }

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

    [Fact]
    public void TripDetailDto_DoesNotInventDurationOrReviewStatistics()
    {
        var dto = new TripDetailDto();

        Assert.Empty(dto.DurationText);
        Assert.Equal(0, dto.Rating);
        Assert.Equal(0, dto.ReviewCount);
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
    public async Task RegisterCustomerAsync_FailsClosedWhenCustomerRoleIsMissing()
    {
        var userRepository = new UserRepositoryWithoutCustomerRole();
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Options);
        var service = new AuthService(
            userRepository,
            new BookingRepository(context),
            new PasswordHasherService());

        var result = await service.RegisterCustomerAsync(new RegisterViewModel
        {
            FullName = "Test Customer",
            Email = "customer@example.com",
            PhoneNumber = "0912345678",
            Password = "test-password",
            ConfirmPassword = "test-password"
        });

        Assert.False(result.Success);
        Assert.Equal(0, userRepository.CreatedUserCount);
    }

    [Fact]
    public async Task PasswordReset_EmailsSingleUseTokenAndUpdatesPassword()
    {
        var passwordHasher = new PasswordHasherService();
        var userRepository = new UserRepositoryWithoutCustomerRole
        {
            ExistingUser = new User
            {
                Id = 17,
                FullName = "Test Customer",
                Email = "customer@example.com",
                PhoneNumber = "0912345678",
                PasswordHash = passwordHasher.HashPassword("old-password"),
                IsActive = true,
                Role = new Role { Name = "Customer" }
            }
        };
        var emailSender = new CapturingPasswordResetEmailSender();
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Options);
        var service = new AuthService(
            userRepository,
            new BookingRepository(context),
            passwordHasher,
            emailSender);

        var requestResult = await service.RequestPasswordResetAsync("customer@example.com");
        var resetResult = await service.ResetPasswordAsync(new ResetPasswordRequestDto
        {
            Token = emailSender.Token!,
            NewPassword = "new-password",
            ConfirmNewPassword = "new-password"
        });

        Assert.True(requestResult.Success);
        Assert.Equal("customer@example.com", emailSender.Recipient);
        Assert.False(string.IsNullOrWhiteSpace(emailSender.Token));
        Assert.True(resetResult.Success);
        Assert.True(passwordHasher.VerifyPassword("new-password", userRepository.ExistingUser.PasswordHash));
        Assert.False((await service.ResetPasswordAsync(new ResetPasswordRequestDto
        {
            Token = emailSender.Token!,
            NewPassword = "another-password",
            ConfirmNewPassword = "another-password"
        })).Success);
    }

    [Fact]
    public async Task SearchAndTripDetails_DoNotInventOperatorHotline()
    {
        var departureDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        var departureTime = new DateTimeOffset(
            departureDate.ToDateTime(new TimeOnly(12, 0)),
            TimeSpan.FromHours(7));
        var trip = new Trip
        {
            Id = 45,
            TripCode = "REFERENCE-TRIP-45",
            DepartureTime = departureTime,
            ExpectedArrivalTime = departureTime.AddHours(6),
            Status = TripStatus.Scheduled,
            Route = new Route
            {
                Id = 3,
                OriginLocationId = 11,
                DestinationLocationId = 12,
                Operator = new Operator { Name = "Reference Operator" }
            }
        };
        var repository = new DummyTripRepository
        {
            SearchResults = new List<Trip> { trip },
            TripDetails = trip
        };
        var service = new TripService(repository, new DummyBookingStore());

        var results = await service.SearchTripsAsync(
            new TripSearchRequestDto(11, 12, departureDate));
        var details = await service.GetTripDetailsAsync(trip.Id);

        Assert.Single(results);
        Assert.Empty(results[0].OperatorHotline);
        Assert.NotNull(details);
        Assert.Empty(details.OperatorHotline);
    }

    [Fact]
    public async Task SearchTripsAsync_DoesNotReturnTripsOutsideRequestedDate()
    {
        var requestedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(2));
        var nearbyDepartureDate = requestedDate.AddDays(1);
        var departureTime = new DateTimeOffset(
            nearbyDepartureDate.ToDateTime(new TimeOnly(12, 0)),
            TimeSpan.FromHours(7));
        var repository = new DummyTripRepository
        {
            SearchResults = new List<Trip>
            {
                new()
                {
                    Id = 46,
                    TripCode = "NEARBY-TRIP-46",
                    DepartureTime = departureTime,
                    ExpectedArrivalTime = departureTime.AddHours(6),
                    Status = TripStatus.Scheduled,
                    Route = new Route
                    {
                        Id = 3,
                        OriginLocationId = 11,
                        DestinationLocationId = 12,
                        Operator = new Operator { Name = "Reference Operator" }
                    }
                }
            }
        };
        var service = new TripService(repository, new DummyBookingStore());

        var results = await service.SearchTripsAsync(
            new TripSearchRequestDto(11, 12, requestedDate));

        Assert.Empty(results);
    }

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

    [Fact]
    public async Task CreateBusWithLayoutAsync_UsesOnlyExplicitSeatLayout()
    {
        var repository = new DummyTripRepository
        {
            BusType = new BusType { Id = 3, Name = "Passenger vehicle" }
        };
        var service = new TripService(repository, new DummyBookingStore());

        await service.CreateBusWithLayoutAsync(new CreateBusDto
        {
            OperatorId = 2,
            BusTypeId = 3,
            LicensePlate = "TEST-001",
            SeatLayoutDefinition = "S01,1,1,1,Seat\r\nS02,1,1,2,Seat\r\nB01,2,1,1,Bed"
        });

        Assert.NotNull(repository.AddedBus);
        Assert.Equal(3, repository.AddedBus.SeatLayout.TotalSeats);
        Assert.Equal(2, repository.AddedBus.SeatLayout.Floors);
        Assert.Collection(
            repository.AddedBus.SeatLayout.Items.OrderBy(item => item.SeatCode),
            item => Assert.Equal(("B01", 2, 1, 1, "Bed"),
                (item.SeatCode, item.Floor, item.Row, item.Column, item.SeatType)),
            item => Assert.Equal(("S01", 1, 1, 1, "Seat"),
                (item.SeatCode, item.Floor, item.Row, item.Column, item.SeatType)),
            item => Assert.Equal(("S02", 1, 1, 2, "Seat"),
                (item.SeatCode, item.Floor, item.Row, item.Column, item.SeatType)));
    }

    [Fact]
    public async Task CreateBusWithLayoutAsync_RejectsDuplicateSeatLocationsBeforeSaving()
    {
        var repository = new DummyTripRepository
        {
            BusType = new BusType { Id = 3, Name = "Passenger vehicle" }
        };
        var service = new TripService(repository, new DummyBookingStore());

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateBusWithLayoutAsync(new CreateBusDto
        {
            OperatorId = 2,
            BusTypeId = 3,
            LicensePlate = "TEST-002",
            SeatLayoutDefinition = "S01,1,1,1,Seat\r\nS02,1,1,1,Seat"
        }));

        Assert.Null(repository.AddedBus);
    }

    [Fact]
    public async Task GetAllTripsAdminAsync_DoesNotShowReferenceSeatsAsAvailableForBooking()
    {
        var repository = new DummyTripRepository
        {
            AdminTrips = new List<Trip>
            {
                new()
                {
                    Id = 1,
                    TripCode = "REFERENCE-ADMIN-1",
                    Status = TripStatus.Scheduled,
                    Bus = new Bus { SeatLayout = new SeatLayout { TotalSeats = 8 } },
                    Seats = new List<TripSeat>
                    {
                        new() { Status = TripSeatStatus.Booked }
                    }
                },
                new()
                {
                    Id = 2,
                    TripCode = "DEMO-ADMIN-2",
                    IsDemoTrip = true,
                    Status = TripStatus.Scheduled,
                    Bus = new Bus { SeatLayout = new SeatLayout { TotalSeats = 8 } },
                    Seats = new List<TripSeat>
                    {
                        new() { Status = TripSeatStatus.Booked }
                    }
                }
            }
        };
        var service = new TripService(repository, new DummyBookingStore());

        var trips = await service.GetAllTripsAdminAsync();

        var reference = Assert.Single(trips, trip => trip.Id == 1);
        Assert.False(reference.IsBookable);
        Assert.Equal(0, reference.AvailableSeats);
        Assert.Equal("Lịch trình tham khảo", reference.OperationalTierLabel);
        var demo = Assert.Single(trips, trip => trip.Id == 2);
        Assert.True(demo.IsBookable);
        Assert.Equal(7, demo.AvailableSeats);
        Assert.Equal("Chuyến xe chạy thử nghiệm (Demo Sandbox)", demo.OperationalTierLabel);
    }

    [Theory]
    [InlineData(-1, 60, 100000)]
    [InlineData(60, 30, 100000)]
    [InlineData(60, 120, 0)]
    public async Task CreateTripAsync_RejectsInvalidTimeOrPrice(
        int departureOffsetMinutes,
        int arrivalOffsetMinutes,
        decimal price)
    {
        var repository = new DummyTripRepository
        {
            RouteOperatorId = 2,
            BusOperatorId = 2
        };
        var service = new TripService(repository, new DummyBookingStore());
        var now = DateTimeOffset.UtcNow;

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateTripAsync(new CreateTripDto
        {
            RouteId = 3,
            BusId = 4,
            DepartureTime = now.AddMinutes(departureOffsetMinutes),
            ExpectedArrivalTime = now.AddMinutes(arrivalOffsetMinutes),
            BasePrice = price
        }));

        Assert.Null(repository.AddedTrip);
    }

    [Fact]
    public async Task CreateTripAsync_SavesValidTripAsNonBookableReferenceSchedule()
    {
        var repository = new DummyTripRepository
        {
            RouteOperatorId = 2,
            BusOperatorId = 2
        };
        var service = new TripService(repository, new DummyBookingStore());
        var departure = DateTimeOffset.UtcNow.AddDays(1);
        var arrival = departure.AddHours(5);

        await service.CreateTripAsync(new CreateTripDto
        {
            RouteId = 3,
            BusId = 4,
            DepartureTime = departure,
            ExpectedArrivalTime = arrival,
            BasePrice = 250000m
        });

        Assert.NotNull(repository.AddedTrip);
        Assert.False(repository.AddedTrip.IsDemoTrip);
        Assert.Equal(TripStatus.Scheduled, repository.AddedTrip.Status);
        Assert.Equal(departure, repository.AddedTrip.DepartureTime);
        Assert.Equal(arrival, repository.AddedTrip.ExpectedArrivalTime);
        Assert.Equal(250000m, repository.AddedTrip.BasePrice);
    }

    [Fact]
    public async Task CreateRouteAsync_RejectsSameOriginAndDestinationBeforeSaving()
    {
        var repository = new DummyTripRepository();
        var service = new TripService(repository, new DummyBookingStore());

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRouteAsync(new CreateRouteDto
        {
            OperatorId = 2,
            OriginLocationId = 10,
            DestinationLocationId = 10,
            Name = "Same location",
            DistanceKm = 50,
            EstimatedDurationMinutes = 60
        }));

        Assert.Null(repository.AddedRoute);
    }

    [Fact]
    public async Task CreateRouteAsync_GeneratesCollisionResistantCodes()
    {
        var repository = new DummyTripRepository
        {
            ActiveLocations = new List<Location>
            {
                new() { Id = 10, IsActive = true },
                new() { Id = 11, IsActive = true }
            }
        };
        var service = new TripService(repository, new DummyBookingStore());
        var request = new CreateRouteDto
        {
            OperatorId = 2,
            OriginLocationId = 10,
            DestinationLocationId = 11,
            Name = "Origin - Destination",
            DistanceKm = 50,
            EstimatedDurationMinutes = 60
        };

        await service.CreateRouteAsync(request);
        await service.CreateRouteAsync(request);

        Assert.Equal(2, repository.AddedRoutes.Count);
        Assert.NotEqual(repository.AddedRoutes[0].Code, repository.AddedRoutes[1].Code);
        Assert.All(repository.AddedRoutes, route => Assert.True(route.Code.Length <= 50));
    }

    [Fact]
    public async Task CreateRouteAsync_RejectsInactiveLocationsBeforeSaving()
    {
        var repository = new DummyTripRepository
        {
            ActiveLocations = new List<Location> { new() { Id = 10, IsActive = true } }
        };
        var service = new TripService(repository, new DummyBookingStore());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRouteAsync(new CreateRouteDto
        {
            OperatorId = 2,
            OriginLocationId = 10,
            DestinationLocationId = 11,
            Name = "Origin - Destination",
            DistanceKm = 50,
            EstimatedDurationMinutes = 60
        }));

        Assert.Null(repository.AddedRoute);
    }

    [Fact]
    public async Task CreateOperatorAsync_RejectsFieldsExceedingDatabaseLimits()
    {
        var repository = new DummyTripRepository();
        var service = new TripService(repository, new DummyBookingStore());

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateOperatorAsync(new CreateOperatorDto
        {
            Code = new string('A', 31),
            Name = "Test operator"
        }));

        Assert.Null(repository.AddedOperator);
    }

    [Fact]
    public async Task CreateTripAsync_GeneratesCollisionResistantCodes()
    {
        var repository = new DummyTripRepository
        {
            RouteOperatorId = 2,
            BusOperatorId = 2
        };
        var service = new TripService(repository, new DummyBookingStore());
        var departure = DateTimeOffset.UtcNow.AddDays(1);
        var request = new CreateTripDto
        {
            RouteId = 3,
            BusId = 4,
            DepartureTime = departure,
            ExpectedArrivalTime = departure.AddHours(5),
            BasePrice = 250000m
        };

        await service.CreateTripAsync(request);
        await service.CreateTripAsync(request);

        Assert.Equal(2, repository.AddedTrips.Count);
        Assert.NotEqual(repository.AddedTrips[0].TripCode, repository.AddedTrips[1].TripCode);
        Assert.All(repository.AddedTrips, trip => Assert.True(trip.TripCode.Length <= 50));
    }

    [Fact]
    public async Task CreateTripAsync_RejectsOverlappingScheduledTripForSameBus()
    {
        var repository = new DummyTripRepository
        {
            RouteOperatorId = 2,
            BusOperatorId = 2,
            HasConflictingTrip = true
        };
        var service = new TripService(repository, new DummyBookingStore());
        var departure = DateTimeOffset.UtcNow.AddDays(1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateTripAsync(new CreateTripDto
        {
            RouteId = 3,
            BusId = 4,
            DepartureTime = departure,
            ExpectedArrivalTime = departure.AddHours(5),
            BasePrice = 250000m
        }));

        Assert.Null(repository.AddedTrip);
    }

    [Fact]
    public async Task GetOperatorsAsync_PreservesVerificationState()
    {
        var repository = new DummyTripRepository
        {
            ActiveOperators = new List<Operator>
            {
                new() { Id = 4, Code = "TEST", Name = "Unverified operator", IsVerified = false }
            }
        };
        var service = new TripService(repository, new DummyBookingStore());

        var operators = await service.GetOperatorsAsync();

        var result = Assert.Single(operators);
        Assert.False(result.IsVerified);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("customer@example.com", false)]
    [InlineData("invalid-email", true)]
    public async Task CreateBookingAsync_RequiresConfirmedContactEmail(
        string email,
        bool confirmed)
    {
        var point = new PickupDropoffPoint
        {
            Id = 20,
            Name = "Test stop",
            Address = "Test address"
        };
        var trip = new Trip
        {
            Id = 5,
            TripCode = "DEMO-EMAIL-CONFIRMATION",
            IsDemoTrip = true,
            Route = new Route
            {
                Stops = new List<RouteStop>
                {
                    new()
                    {
                        PointId = point.Id,
                        Point = point,
                        StopOrder = 1,
                        AllowPickup = true,
                        AllowDropoff = true
                    }
                },
                OriginLocation = new Location { Name = "Origin" },
                DestinationLocation = new Location { Name = "Destination" }
            }
        };
        var repository = new StopPermissionBookingRepository(trip);
        var service = new BookingService(repository, new FixedBookingSessionContext());
        var request = new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = "HOLD-email-test",
            SeatCodes = new List<string> { "A01" },
            PassengerName = "Test Passenger",
            PassengerPhone = "0912345678",
            PassengerEmail = email,
            ContactEmailConfirmed = confirmed
        };

        var result = await service.CreateBookingAsync(request);

        Assert.False(result.Success);
        Assert.Contains("email", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(repository.CreateBookingCalled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateBookingAsync_TripStopPermissionOverridesRouteStop(bool pickup)
    {
        var point = new PickupDropoffPoint
        {
            Id = 20,
            Name = "Test stop",
            Address = "Test address"
        };
        var trip = new Trip
        {
            Id = 5,
            TripCode = "DEMO-STOP-PERMISSION",
            IsDemoTrip = true,
            Route = new Route
            {
                Stops = new List<RouteStop>
                {
                    new()
                    {
                        PointId = point.Id,
                        Point = point,
                        StopOrder = 1,
                        AllowPickup = pickup,
                        AllowDropoff = !pickup
                    }
                },
                OriginLocation = new Location { Name = "Origin" },
                DestinationLocation = new Location { Name = "Destination" }
            },
            Stops = new List<TripStop>
            {
                new()
                {
                    PointId = point.Id,
                    Point = point,
                    StopOrder = 1,
                    AllowPickup = false,
                    AllowDropoff = false
                }
            }
        };
        var repository = new StopPermissionBookingRepository(trip);
        var service = new BookingService(repository, new FixedBookingSessionContext());
        var request = new TestCreateBookingRequestDto
        {
            TripId = trip.Id,
            HoldToken = "HOLD-permission-test",
            SeatCodes = new List<string> { "A01" },
            PassengerName = "Test Passenger",
            PassengerPhone = "0912345678",
            PickupPointId = pickup ? point.Id : null,
            DropoffPointId = pickup ? null : point.Id
        };

        var result = await service.CreateBookingAsync(request);

        Assert.False(result.Success);
        Assert.False(repository.CreateBookingCalled);
    }

    private class DummyTripRepository : Tripgo.Application.Interfaces.Persistence.ITripRepository
    {
        public List<Trip> SearchResults { get; init; } = new();
        public Trip? TripDetails { get; init; }
        public List<Trip> AdminTrips { get; init; } = new();
        public BusType? BusType { get; init; }
        public Bus? AddedBus { get; private set; }
        public long? RouteOperatorId { get; init; }
        public long? BusOperatorId { get; init; }
        public Trip? AddedTrip { get; private set; }
        public List<Trip> AddedTrips { get; } = new();
        public bool HasConflictingTrip { get; init; }
        public Route? AddedRoute { get; private set; }
        public List<Route> AddedRoutes { get; } = new();
        public List<Location> ActiveLocations { get; init; } = new();
        public List<Operator> ActiveOperators { get; init; } = new();
        public Operator? AddedOperator { get; private set; }

        public Task<List<Location>> GetActiveLocationsAsync(System.Threading.CancellationToken ct = default) => Task.FromResult(ActiveLocations);
        public Task<List<Operator>> GetActiveOperatorsAsync(System.Threading.CancellationToken ct = default) => Task.FromResult(ActiveOperators);
        public Task<bool> OperatorExistsAndActiveAsync(long id, System.Threading.CancellationToken ct = default) => Task.FromResult(true);
        public Task AddOperatorAsync(Operator operatorEntity, System.Threading.CancellationToken ct = default) { AddedOperator = operatorEntity; return Task.CompletedTask; }
        public Task<List<BusType>> GetActiveBusTypesAsync(System.Threading.CancellationToken ct = default) => Task.FromResult(new List<BusType>());
        public Task<BusType?> GetActiveBusTypeByIdAsync(long id, System.Threading.CancellationToken ct = default) => Task.FromResult(BusType?.Id == id ? BusType : null);
        public Task<List<Bus>> GetActiveBusesByOperatorAsync(long operatorId, System.Threading.CancellationToken ct = default) => Task.FromResult(new List<Bus>());
        public Task<bool> BusExistsAndActiveAsync(long id, System.Threading.CancellationToken ct = default) => Task.FromResult(true);
        public Task<long?> GetBusOperatorIdAsync(long busId, System.Threading.CancellationToken ct = default) => Task.FromResult(BusOperatorId);
        public Task AddBusAsync(Bus bus, System.Threading.CancellationToken ct = default) { AddedBus = bus; return Task.CompletedTask; }
        public Task<List<Route>> GetActiveRoutesAsync(System.Threading.CancellationToken ct = default) => Task.FromResult(new List<Route>());
        public Task<long?> GetRouteOperatorIdAsync(long routeId, System.Threading.CancellationToken ct = default) => Task.FromResult(RouteOperatorId);
        public Task AddRouteAsync(Route route, System.Threading.CancellationToken ct = default) { AddedRoute = route; AddedRoutes.Add(route); return Task.CompletedTask; }
        public Task<List<Trip>> SearchTripsAsync(long originLocationId, long destinationLocationId, DateTimeOffset windowStart, DateTimeOffset windowEnd, System.Threading.CancellationToken ct = default) => Task.FromResult(SearchResults);
        public Task<Trip?> GetTripWithDetailsAsync(long tripId, System.Threading.CancellationToken ct = default) => Task.FromResult(TripDetails);
        public Task<List<Trip>> GetAllTripsAdminAsync(System.Threading.CancellationToken ct = default) => Task.FromResult(AdminTrips);
        public Task<Trip?> GetTripForBookingAsync(long tripId, System.Threading.CancellationToken ct = default) => Task.FromResult<Trip?>(null);
        public Task<bool> HasConflictingScheduledTripAsync(long busId, DateTimeOffset departureTime, DateTimeOffset arrivalTime, System.Threading.CancellationToken ct = default) => Task.FromResult(HasConflictingTrip);
        public Task AddTripAsync(Trip trip, System.Threading.CancellationToken ct = default) { AddedTrip = trip; AddedTrips.Add(trip); return Task.CompletedTask; }
        public Task<bool> CancelTripAsync(long tripId, System.Threading.CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class FixedBookingSessionContext : IBookingSessionContext
    {
        public Task<string?> GetOwnerIdentifierAsync() =>
            Task.FromResult<string?>("session-owner");

        public Task<long?> GetUserIdAsync() =>
            Task.FromResult<long?>(null);
    }

    private sealed class StopPermissionBookingRepository : IBookingRepository
    {
        private readonly Trip _trip;
        public bool CreateBookingCalled { get; private set; }

        public StopPermissionBookingRepository(Trip trip) => _trip = trip;

        public Task<Trip?> GetTripForBookingAsync(long tripId, CancellationToken ct = default) =>
            Task.FromResult<Trip?>(_trip);

        public Task<List<TripSeat>> GetTripSeatsAsync(long tripId, CancellationToken ct = default) =>
            Task.FromResult(new List<TripSeat>
            {
                new() { Id = 1, SeatCode = "A01", Price = 100_000m }
            });

        public Task AddTripSeatsAsync(IEnumerable<TripSeat> seats, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<SeatHoldResultDto> HoldSeatsAsync(
            long tripId, List<string> seatCodes, string holdToken, int holdMinutes,
            string? ownerIdentifier = null, CancellationToken ct = default) =>
            Task.FromResult(new SeatHoldResultDto());

        public Task<bool> ReleaseHoldAsync(
            string holdToken, string ownerIdentifier, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<BookingResultDto> CreateBookingAsync(
            Booking booking, List<BookingSeat> seats, List<BookingPassenger> passengers,
            string holdToken, string? ownerIdentifier = null, CancellationToken ct = default)
        {
            CreateBookingCalled = true;
            return Task.FromResult(new BookingResultDto { Success = true });
        }

        public Task<Booking?> GetBookingByCodeAsync(
            string bookingCode, string? ownerIdentifier, long? userId = null,
            CancellationToken ct = default) =>
            Task.FromResult<Booking?>(null);

        public Task<List<Booking>> GetBookingsByPhoneAsync(
            string phone, string? ownerIdentifier, long? userId = null,
            CancellationToken ct = default) =>
            Task.FromResult(new List<Booking>());

        public Task<List<Booking>> GetBookingsByUserIdAsync(
            long userId, CancellationToken ct = default) =>
            Task.FromResult(new List<Booking>());

        public Task<List<Booking>> GetBookingsByOwnerIdentifierAsync(
            string ownerIdentifier, CancellationToken ct = default) =>
            Task.FromResult(new List<Booking>());

        public Task<int> ClaimGuestBookingsAsync(
            long userId, string guestOwnerIdentifier, CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task ExpireStaleHoldsAsync(CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task ExpireStaleBookingsAsync(CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<bool> CancelBookingAsync(
            long bookingId, string? ownerIdentifier, long? userId, string reason,
            CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<PaymentProcessResultDto> ConfirmPaymentAndBookingAsync(
            string bookingCode, string transactionCode, string paymentMethod,
            decimal amount, bool isSuccess, string? failureReason,
            string? ownerIdentifier, long? userId = null, CancellationToken ct = default) =>
            Task.FromResult(new PaymentProcessResultDto());

        public Task<List<PaymentTransaction>> GetPaymentTransactionsByBookingCodeAsync(
            string bookingCode, string? ownerIdentifier, long? userId = null,
            CancellationToken ct = default) =>
            Task.FromResult(new List<PaymentTransaction>());
    }

    private sealed class UserRepositoryWithoutCustomerRole : IUserRepository
    {
        public int CreatedUserCount { get; private set; }
        public User? ExistingUser { get; set; }

        public Task<User?> GetByIdAsync(long id, CancellationToken ct = default) =>
            Task.FromResult(ExistingUser?.Id == id ? ExistingUser : null);

        public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
            Task.FromResult(
                ExistingUser != null &&
                string.Equals(ExistingUser.Email, email, StringComparison.OrdinalIgnoreCase)
                    ? ExistingUser
                    : null);

        public Task<User?> GetByPhoneAsync(string phone, CancellationToken ct = default) =>
            Task.FromResult<User?>(null);

        public Task<User?> GetByEmailOrPhoneAsync(string identifier, CancellationToken ct = default) =>
            Task.FromResult<User?>(null);

        public Task<bool> ExistsByEmailAsync(string email, long? excludeUserId = null, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<bool> ExistsByPhoneAsync(string phone, long? excludeUserId = null, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<User> CreateAsync(User user, CancellationToken ct = default)
        {
            CreatedUserCount++;
            return Task.FromResult(user);
        }

        public Task<bool> UpdateAsync(User user, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<Role?> GetRoleByNameAsync(string roleName, CancellationToken ct = default) =>
            Task.FromResult<Role?>(null);

        public Task<List<Role>> GetAllRolesAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<Role> { new() { Id = 2, Name = "Admin" } });
    }

    private sealed class CapturingPasswordResetEmailSender : IEmailSender
    {
        public string? Recipient { get; private set; }
        public string? Token { get; private set; }

        public Task<bool> SendPasswordResetAsync(
            string email,
            string token,
            CancellationToken cancellationToken = default)
        {
            Recipient = email;
            Token = token;
            return Task.FromResult(true);
        }

        public Task<bool> SendBookingConfirmationAsync(
            BookingConfirmationEmailDto confirmation,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private class DummyBookingStore : Tripgo.Application.Services.Temporary.ITemporaryBookingStore
    {
        public int GetBookedSeatCount(long tripId) => 0;
        public HashSet<string> GetBookedSeatSnapshot(long tripId) => new();
        public bool TryBookSeats(long tripId, List<string> internalSeatCodes, List<string> requestedCodes, CustomerBookingInfoDto customerInfo, string routeName, string operatorName, string busTypeName, string busLicensePlate, DateTimeOffset departureTime, decimal basePrice) => false;
        public List<CustomerBookingInfoDto> GetBookingsByPhone(string normalizedPhone) => new();
        public CustomerBookingInfoDto? GetBookingByCode(string bookingCode) => null;
        public void RemoveTripBookedSeats(long tripId) { }
    }
}