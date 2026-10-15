using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
public class CustomerAuthenticationAndOwnershipTests : IAsyncLifetime
{
    private const string IsolatedConnectionString =
        "Server=localhost\\MSSQLSERVER01;Database=TripgoDb_IsolatedTest;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true;";

    private readonly string _runSuffix = Guid.NewGuid().ToString("N")[..8];
    private readonly List<long> _createdUserIds = new();
    private readonly List<string> _createdBookingCodes = new();

    private AppDbContext CreateContext()
    {
        var builder = new SqlConnectionStringBuilder(IsolatedConnectionString);
        if (!string.Equals(builder.InitialCatalog, "TripgoDb_IsolatedTest", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("STRICT DATABASE SAFETY: Tests must run ONLY against TripgoDb_IsolatedTest!");
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(IsolatedConnectionString, sql => sql.EnableRetryOnFailure())
            .Options;
        return new AppDbContext(options);
    }

    private sealed class TestBookingSessionContext : IBookingSessionContext
    {
        private readonly string? _ownerIdentifier;
        private readonly long? _userId;

        public TestBookingSessionContext(string? ownerIdentifier, long? userId = null)
        {
            _ownerIdentifier = ownerIdentifier;
            _userId = userId;
        }

        public Task<string?> GetOwnerIdentifierAsync() => Task.FromResult(_ownerIdentifier);
        public Task<long?> GetUserIdAsync() => Task.FromResult(_userId);
    }

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        var fixture = new SandboxFixtureService(context, NullLogger<SandboxFixtureService>.Instance);
        await fixture.EnsureRolesAndDefaultUsersAsync();
        await fixture.RepairPickupDropoffUnicodeAsync();
        await fixture.EnsureSandboxTripsAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();

        if (_createdBookingCodes.Count > 0)
        {
            var bookings = await context.Bookings
                .Include(b => b.BookingSeats)
                .Where(b => _createdBookingCodes.Contains(b.BookingCode))
                .ToListAsync();

            var bIds = bookings.Select(b => b.Id).ToList();
            await context.PaymentTransactions.Where(p => bIds.Contains(p.BookingId)).ExecuteDeleteAsync();
            await context.BookingPassengers.Where(bp => bIds.Contains(bp.BookingId)).ExecuteDeleteAsync();
            await context.BookingSeats.Where(bs => bIds.Contains(bs.BookingId)).ExecuteDeleteAsync();
            await context.SeatHolds.Where(h => bookings.Select(b => b.HoldToken).Contains(h.HoldToken)).ExecuteDeleteAsync();
            await context.Bookings.Where(b => bIds.Contains(b.Id)).ExecuteDeleteAsync();
        }

        if (_createdUserIds.Count > 0)
        {
            await context.Users.Where(u => _createdUserIds.Contains(u.Id)).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Test01_CustomerRegistration_Success_CreatesCustomerWithHashedPassword()
    {
        await using var context = CreateContext();
        var userRepo = new UserRepository(context);
        var bookingRepo = new BookingRepository(context);
        var hasher = new PasswordHasherService();
        var authService = new AuthService(userRepo, bookingRepo, hasher);

        var regEmail = $"testuser_{_runSuffix}@tripgo.vn";
        var regPhone = $"09{new Random().Next(10000000, 99999999)}";

        var regModel = new RegisterViewModel
        {
            FullName = "Nguyễn Văn Test",
            Email = regEmail,
            PhoneNumber = regPhone,
            Password = "Password123@",
            ConfirmPassword = "Password123@"
        };

        var result = await authService.RegisterCustomerAsync(regModel);

        Assert.True(result.Success);
        Assert.NotNull(result.UserId);
        Assert.Equal("Customer", result.Role);
        _createdUserIds.Add(result.UserId!.Value);

        // Verify in database: Password must be hashed (never plaintext)
        var userInDb = await userRepo.GetByIdAsync(result.UserId.Value);
        Assert.NotNull(userInDb);
        Assert.NotEqual("Password123@", userInDb.PasswordHash);
        Assert.True(hasher.VerifyPassword("Password123@", userInDb.PasswordHash));
        Assert.Equal("Customer", userInDb.Role?.Name);
    }

    [Fact]
    public async Task Test02_CustomerRegistration_DuplicateEmail_Rejected()
    {
        await using var context = CreateContext();
        var userRepo = new UserRepository(context);
        var bookingRepo = new BookingRepository(context);
        var hasher = new PasswordHasherService();
        var authService = new AuthService(userRepo, bookingRepo, hasher);

        var regEmail = $"dupemail_{_runSuffix}@tripgo.vn";
        var phone1 = $"09{new Random().Next(10000000, 99999999)}";
        var phone2 = $"09{new Random().Next(10000000, 99999999)}";

        var res1 = await authService.RegisterCustomerAsync(new RegisterViewModel
        {
            FullName = "User First",
            Email = regEmail,
            PhoneNumber = phone1,
            Password = "Password123@",
            ConfirmPassword = "Password123@"
        });
        Assert.True(res1.Success);
        _createdUserIds.Add(res1.UserId!.Value);

        // Attempt second registration with same email
        var res2 = await authService.RegisterCustomerAsync(new RegisterViewModel
        {
            FullName = "User Second",
            Email = regEmail.ToUpperInvariant(), // test case-insensitivity
            PhoneNumber = phone2,
            Password = "Password123@",
            ConfirmPassword = "Password123@"
        });

        Assert.False(res2.Success);
        Assert.Contains("email", res2.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Test03_CustomerRegistration_RoleEscalationPrevented_AlwaysCustomer()
    {
        await using var context = CreateContext();
        var userRepo = new UserRepository(context);
        var bookingRepo = new BookingRepository(context);
        var hasher = new PasswordHasherService();
        var authService = new AuthService(userRepo, bookingRepo, hasher);

        var regEmail = $"noescalate_{_runSuffix}@tripgo.vn";
        var regPhone = $"09{new Random().Next(10000000, 99999999)}";

        var res = await authService.RegisterCustomerAsync(new RegisterViewModel
        {
            FullName = "Hacker Aspirant",
            Email = regEmail,
            PhoneNumber = regPhone,
            Password = "Password123@",
            ConfirmPassword = "Password123@"
        });

        Assert.True(res.Success);
        _createdUserIds.Add(res.UserId!.Value);

        var userInDb = await userRepo.GetByIdAsync(res.UserId.Value);
        Assert.NotNull(userInDb);
        Assert.Equal("Customer", userInDb.Role?.Name);
        Assert.NotEqual("Admin", userInDb.Role?.Name);
    }

    [Fact]
    public async Task Test04_CustomerLogin_ValidCredentials_Success()
    {
        await using var context = CreateContext();
        var userRepo = new UserRepository(context);
        var bookingRepo = new BookingRepository(context);
        var hasher = new PasswordHasherService();
        var authService = new AuthService(userRepo, bookingRepo, hasher);

        var regEmail = $"loginuser_{_runSuffix}@tripgo.vn";
        var regPhone = $"09{new Random().Next(10000000, 99999999)}";

        var reg = await authService.RegisterCustomerAsync(new RegisterViewModel
        {
            FullName = "Đăng Nhập Thành Công",
            Email = regEmail,
            PhoneNumber = regPhone,
            Password = "SecretPassword123!",
            ConfirmPassword = "SecretPassword123!"
        });
        Assert.True(reg.Success);
        _createdUserIds.Add(reg.UserId!.Value);

        // Login with email
        var loginRes = await authService.AuthenticateAsync(new LoginViewModel
        {
            AccountIdentifier = regEmail,
            Password = "SecretPassword123!"
        });

        Assert.True(loginRes.Success);
        Assert.Equal(reg.UserId, loginRes.UserId);
        Assert.Equal("Customer", loginRes.Role);

        // Login with phone number
        var loginPhoneRes = await authService.AuthenticateAsync(new LoginViewModel
        {
            AccountIdentifier = regPhone,
            Password = "SecretPassword123!"
        });

        Assert.True(loginPhoneRes.Success);
        Assert.Equal(reg.UserId, loginPhoneRes.UserId);
    }

    [Fact]
    public async Task Test05_CustomerLogin_InvalidPassword_Fails()
    {
        await using var context = CreateContext();
        var userRepo = new UserRepository(context);
        var bookingRepo = new BookingRepository(context);
        var hasher = new PasswordHasherService();
        var authService = new AuthService(userRepo, bookingRepo, hasher);

        var regEmail = $"wrongpass_{_runSuffix}@tripgo.vn";
        var regPhone = $"09{new Random().Next(10000000, 99999999)}";

        var reg = await authService.RegisterCustomerAsync(new RegisterViewModel
        {
            FullName = "Khách Hàng Sai Mật Khẩu",
            Email = regEmail,
            PhoneNumber = regPhone,
            Password = "CorrectPassword123",
            ConfirmPassword = "CorrectPassword123"
        });
        _createdUserIds.Add(reg.UserId!.Value);

        var loginRes = await authService.AuthenticateAsync(new LoginViewModel
        {
            AccountIdentifier = regEmail,
            Password = "WrongPassword999"
        });

        Assert.False(loginRes.Success);
        Assert.Contains("không chính xác", loginRes.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Test06_CustomerProfile_ViewAndUpdate_Success()
    {
        await using var context = CreateContext();
        var userRepo = new UserRepository(context);
        var bookingRepo = new BookingRepository(context);
        var hasher = new PasswordHasherService();
        var authService = new AuthService(userRepo, bookingRepo, hasher);

        var email = $"profile_{_runSuffix}@tripgo.vn";
        var phone = $"09{new Random().Next(10000000, 99999999)}";

        var reg = await authService.RegisterCustomerAsync(new RegisterViewModel
        {
            FullName = "Tên Ban Đầu",
            Email = email,
            PhoneNumber = phone,
            Password = "Password123@",
            ConfirmPassword = "Password123@"
        });
        _createdUserIds.Add(reg.UserId!.Value);

        // Get Profile
        var profile = await authService.GetUserProfileAsync(reg.UserId.Value);
        Assert.NotNull(profile);
        Assert.Equal("Tên Ban Đầu", profile.FullName);

        // Update Profile
        var newPhone = $"09{new Random().Next(10000000, 99999999)}";
        var updateRes = await authService.UpdateProfileAsync(reg.UserId.Value, new UpdateProfileRequestDto
        {
            FullName = "Tên Đã Đổi",
            PhoneNumber = newPhone
        });

        Assert.True(updateRes.Success);

        // Verify update persisted
        var updatedProfile = await authService.GetUserProfileAsync(reg.UserId.Value);
        Assert.NotNull(updatedProfile);
        Assert.Equal("Tên Đã Đổi", updatedProfile.FullName);
        Assert.Equal(newPhone, updatedProfile.PhoneNumber);
    }

    [Fact]
    public async Task Test07_CustomerProfile_ChangePassword_WorksCorrectly()
    {
        await using var context = CreateContext();
        var userRepo = new UserRepository(context);
        var bookingRepo = new BookingRepository(context);
        var hasher = new PasswordHasherService();
        var authService = new AuthService(userRepo, bookingRepo, hasher);

        var email = $"changepass_{_runSuffix}@tripgo.vn";
        var phone = $"09{new Random().Next(10000000, 99999999)}";

        var reg = await authService.RegisterCustomerAsync(new RegisterViewModel
        {
            FullName = "Đổi Mật Khẩu User",
            Email = email,
            PhoneNumber = phone,
            Password = "OldPassword123@",
            ConfirmPassword = "OldPassword123@"
        });
        _createdUserIds.Add(reg.UserId!.Value);

        // Change password with wrong current password -> fails
        var failChange = await authService.ChangePasswordAsync(reg.UserId.Value, new ChangePasswordRequestDto
        {
            CurrentPassword = "IncorrectCurrentPassword",
            NewPassword = "NewPassword456@",
            ConfirmNewPassword = "NewPassword456@"
        });
        Assert.False(failChange.Success);

        // Change password with correct current password -> succeeds
        var successChange = await authService.ChangePasswordAsync(reg.UserId.Value, new ChangePasswordRequestDto
        {
            CurrentPassword = "OldPassword123@",
            NewPassword = "NewPassword456@",
            ConfirmNewPassword = "NewPassword456@"
        });
        Assert.True(successChange.Success);

        // Login with old password fails
        var loginOld = await authService.AuthenticateAsync(new LoginViewModel
        {
            AccountIdentifier = email,
            Password = "OldPassword123@"
        });
        Assert.False(loginOld.Success);

        // Login with new password succeeds
        var loginNew = await authService.AuthenticateAsync(new LoginViewModel
        {
            AccountIdentifier = email,
            Password = "NewPassword456@"
        });
        Assert.True(loginNew.Success);
    }

    [Fact]
    public async Task Test08_AuthenticatedBooking_AssignsUserId_PersistsInSql()
    {
        await using var context = CreateContext();
        var userRepo = new UserRepository(context);
        var bookingRepo = new BookingRepository(context);
        var hasher = new PasswordHasherService();
        var authService = new AuthService(userRepo, bookingRepo, hasher);

        // 1. Create authenticated customer
        var email = $"authbook_{_runSuffix}@tripgo.vn";
        var phone = $"09{new Random().Next(10000000, 99999999)}";
        var reg = await authService.RegisterCustomerAsync(new RegisterViewModel
        {
            FullName = "Khách Hàng Đã Đăng Nhập",
            Email = email,
            PhoneNumber = phone,
            Password = "Password123@",
            ConfirmPassword = "Password123@"
        });
        var userId = reg.UserId!.Value;
        _createdUserIds.Add(userId);

        // 2. Set up booking session context with authenticated userId
        var ownerGuid = Guid.NewGuid().ToString("N");
        var sessionContext = new TestBookingSessionContext(ownerGuid, userId);
        var bookingService = new BookingService(bookingRepo, sessionContext);

        // Find an active sandbox trip
        var trip = await context.Trips.Include(t => t.Seats).FirstAsync(t => t.IsDemoTrip && t.Seats.Any());
        await bookingService.EnsureDemoTripInventoryAsync(trip.Id);

        var seats = await bookingService.GetSeatsForTripAsync(trip.Id);
        var seatToBook = seats.First(s => s.IsAvailable);

        var holdRes = await bookingService.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seatToBook.SeatCode },
            HoldMinutes = 15
        });
        Assert.True(holdRes.Success);
        Assert.NotNull(holdRes.HoldToken);

        var createRes = await bookingService.CreateBookingAsync(new CreateBookingRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seatToBook.SeatCode },
            HoldToken = holdRes.HoldToken!,
            PassengerName = "Khách Hàng Đã Đăng Nhập",
            PassengerPhone = phone,
            PassengerEmail = email
        });

        Assert.True(createRes.Success);
        _createdBookingCodes.Add(createRes.BookingCode);

        // Verify in database: Booking MUST have UserId persisted and matching the authenticated customer
        var bookingInDb = await context.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.BookingCode == createRes.BookingCode);
        Assert.NotNull(bookingInDb);
        Assert.Equal(userId, bookingInDb.UserId);
    }

    [Fact]
    public async Task Test09_MyTickets_LoadsAutomaticallyByUserId_WithoutBookingCode()
    {
        await using var context = CreateContext();
        var userRepo = new UserRepository(context);
        var bookingRepo = new BookingRepository(context);
        var hasher = new PasswordHasherService();
        var authService = new AuthService(userRepo, bookingRepo, hasher);

        var email = $"mytickets_{_runSuffix}@tripgo.vn";
        var phone = $"09{new Random().Next(10000000, 99999999)}";
        var reg = await authService.RegisterCustomerAsync(new RegisterViewModel
        {
            FullName = "Khách Xem Vé",
            Email = email,
            PhoneNumber = phone,
            Password = "Password123@",
            ConfirmPassword = "Password123@"
        });
        var userId = reg.UserId!.Value;
        _createdUserIds.Add(userId);

        var sessionContext = new TestBookingSessionContext(Guid.NewGuid().ToString("N"), userId);
        var bookingService = new BookingService(bookingRepo, sessionContext);

        var trip = await context.Trips.Include(t => t.Seats).FirstAsync(t => t.IsDemoTrip && t.Seats.Any());
        await bookingService.EnsureDemoTripInventoryAsync(trip.Id);
        var availableSeats = await bookingService.GetSeatsForTripAsync(trip.Id);
        var seat = availableSeats.First(s => s.IsAvailable);

        var holdRes = await bookingService.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seat.SeatCode },
            HoldMinutes = 15
        });
        Assert.True(holdRes.Success);
        Assert.NotNull(holdRes.HoldToken);

        var createRes = await bookingService.CreateBookingAsync(new CreateBookingRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seat.SeatCode },
            HoldToken = holdRes.HoldToken!,
            PassengerName = "Khách Xem Vé",
            PassengerPhone = phone,
            PassengerEmail = email
        });
        _createdBookingCodes.Add(createRes.BookingCode);

        // Call GetBookingsForCurrentUserAsync WITHOUT specifying any booking code or phone number
        var userTickets = await bookingService.GetBookingsForCurrentUserAsync();

        Assert.NotEmpty(userTickets);
        Assert.Contains(userTickets, t => t.BookingCode == createRes.BookingCode);
    }

    [Fact]
    public async Task Test10_OwnershipIsolation_UserCannotAccessOrCancelOtherUsersBooking()
    {
        await using var context = CreateContext();
        var userRepo = new UserRepository(context);
        var bookingRepo = new BookingRepository(context);
        var hasher = new PasswordHasherService();
        var authService = new AuthService(userRepo, bookingRepo, hasher);

        // Account A
        var regA = await authService.RegisterCustomerAsync(new RegisterViewModel
        {
            FullName = "Người Dùng A",
            Email = $"usera_{_runSuffix}@tripgo.vn",
            PhoneNumber = $"09{new Random().Next(10000000, 99999999)}",
            Password = "Password123@",
            ConfirmPassword = "Password123@"
        });
        var userAId = regA.UserId!.Value;
        _createdUserIds.Add(userAId);

        // Account B
        var regB = await authService.RegisterCustomerAsync(new RegisterViewModel
        {
            FullName = "Người Dùng B",
            Email = $"userb_{_runSuffix}@tripgo.vn",
            PhoneNumber = $"09{new Random().Next(10000000, 99999999)}",
            Password = "Password123@",
            ConfirmPassword = "Password123@"
        });
        var userBId = regB.UserId!.Value;
        _createdUserIds.Add(userBId);

        // User A creates booking
        var sessionA = new TestBookingSessionContext(Guid.NewGuid().ToString("N"), userAId);
        var bookingServiceA = new BookingService(bookingRepo, sessionA);

        var trip = await context.Trips.Include(t => t.Seats).FirstAsync(t => t.IsDemoTrip && t.Seats.Any());
        await bookingServiceA.EnsureDemoTripInventoryAsync(trip.Id);
        var availableSeats = await bookingServiceA.GetSeatsForTripAsync(trip.Id);
        var seat = availableSeats.First(s => s.IsAvailable);

        var holdRes = await bookingServiceA.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seat.SeatCode },
            HoldMinutes = 15
        });
        Assert.True(holdRes.Success);
        Assert.NotNull(holdRes.HoldToken);

        var createRes = await bookingServiceA.CreateBookingAsync(new CreateBookingRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seat.SeatCode },
            HoldToken = holdRes.HoldToken!,
            PassengerName = "Người Dùng A",
            PassengerPhone = "0900000002"
        });
        _createdBookingCodes.Add(createRes.BookingCode);

        var dbBooking = await context.Bookings.FirstAsync(b => b.BookingCode == createRes.BookingCode);

        // User B attempts to access User A's booking
        var sessionB = new TestBookingSessionContext(Guid.NewGuid().ToString("N"), userBId);
        var bookingServiceB = new BookingService(bookingRepo, sessionB);

        var viewAttempt = await bookingServiceB.GetBookingByCodeAsync(createRes.BookingCode);
        Assert.Null(viewAttempt); // Must return null, completely isolated

        var cancelAttempt = await bookingServiceB.CancelBookingAsync(dbBooking.Id, "User B cố tình hủy vé của User A");
        Assert.False(cancelAttempt); // Must reject cancellation
    }

    [Fact]
    public async Task Test11_GuestBooking_CanBeClaimedByAccount_AfterRegistrationOrLogin()
    {
        await using var context = CreateContext();
        var userRepo = new UserRepository(context);
        var bookingRepo = new BookingRepository(context);
        var hasher = new PasswordHasherService();
        var authService = new AuthService(userRepo, bookingRepo, hasher);

        // 1. Guest creates a booking without being logged in
        var guestOwner = Guid.NewGuid().ToString("N");
        var guestSession = new TestBookingSessionContext(guestOwner, null);
        var guestBookingService = new BookingService(bookingRepo, guestSession);

        var trip = await context.Trips.Include(t => t.Seats).FirstAsync(t => t.IsDemoTrip && t.Seats.Any());
        await guestBookingService.EnsureDemoTripInventoryAsync(trip.Id);
        var availableSeats = await guestBookingService.GetSeatsForTripAsync(trip.Id);
        var seat = availableSeats.First(s => s.IsAvailable);

        var holdRes = await guestBookingService.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seat.SeatCode },
            HoldMinutes = 15
        });
        Assert.True(holdRes.Success);
        Assert.NotNull(holdRes.HoldToken);

        var createRes = await guestBookingService.CreateBookingAsync(new CreateBookingRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seat.SeatCode },
            HoldToken = holdRes.HoldToken!,
            PassengerName = "Khách Vãng Lai",
            PassengerPhone = "0987654321"
        });
        _createdBookingCodes.Add(createRes.BookingCode);

        // Verify booking in DB has UserId == null
        var guestBookingInDb = await context.Bookings.FirstAsync(b => b.BookingCode == createRes.BookingCode);
        Assert.Null(guestBookingInDb.UserId);
        Assert.Equal(guestOwner, guestBookingInDb.OwnerIdentifier);

        // 2. Guest now registers an account
        var reg = await authService.RegisterCustomerAsync(new RegisterViewModel
        {
            FullName = "Khách Vãng Lai Thành Viên",
            Email = $"claimtest_{_runSuffix}@tripgo.vn",
            PhoneNumber = $"09{new Random().Next(10000000, 99999999)}",
            Password = "Password123@",
            ConfirmPassword = "Password123@"
        });
        var newUserId = reg.UserId!.Value;
        _createdUserIds.Add(newUserId);

        // 3. Claim guest bookings using the guest owner identifier
        var claimedCount = await authService.ClaimGuestBookingsAsync(newUserId, guestOwner);
        Assert.True(claimedCount > 0);

        // Verify booking in DB now has UserId assigned
        var updatedBooking = await context.Bookings.AsNoTracking().FirstAsync(b => b.BookingCode == createRes.BookingCode);
        Assert.Equal(newUserId, updatedBooking.UserId);
    }

    [Fact]
    public async Task Test12_CancelBooking_WhenPendingPayment_ReleasesSeatAndUpdatesStatus()
    {
        await using var context = CreateContext();
        var bookingRepo = new BookingRepository(context);

        var owner = Guid.NewGuid().ToString("N");
        var session = new TestBookingSessionContext(owner, null);
        var bookingService = new BookingService(bookingRepo, session);

        var trip = await context.Trips.Include(t => t.Seats).FirstAsync(t => t.IsDemoTrip && t.Seats.Any());
        await bookingService.EnsureDemoTripInventoryAsync(trip.Id);
        var availableSeats = await bookingService.GetSeatsForTripAsync(trip.Id);
        var seat = availableSeats.First(s => s.IsAvailable);

        var holdRes = await bookingService.HoldSeatsAsync(new SeatHoldRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seat.SeatCode },
            HoldMinutes = 15
        });
        Assert.True(holdRes.Success);
        Assert.NotNull(holdRes.HoldToken);

        var createRes = await bookingService.CreateBookingAsync(new CreateBookingRequestDto
        {
            TripId = trip.Id,
            SeatCodes = new List<string> { seat.SeatCode },
            HoldToken = holdRes.HoldToken!,
            PassengerName = "Người Hủy Vé",
            PassengerPhone = "0911223344"
        });
        _createdBookingCodes.Add(createRes.BookingCode);

        var dbBooking = await context.Bookings.FirstAsync(b => b.BookingCode == createRes.BookingCode);
        Assert.Equal(BookingStatus.PendingPayment, dbBooking.Status);

        // Cancel the booking
        var cancelSuccess = await bookingService.CancelBookingAsync(dbBooking.Id, "Khách hàng đổi lịch trình");
        Assert.True(cancelSuccess);

        // Verify status is Cancelled and seat is returned to Available
        var reloadedBooking = await context.Bookings.AsNoTracking().FirstAsync(b => b.Id == dbBooking.Id);
        Assert.Equal(BookingStatus.Cancelled, reloadedBooking.Status);

        var seatInDb = await context.TripSeats.AsNoTracking().FirstAsync(s => s.Id == seat.Id);
        Assert.Equal(TripSeatStatus.Available, seatInDb.Status);
    }

    [Fact]
    public async Task Test13_VietnameseUnicodeFix_PickupDropoffPoints_AuthoritativeStringsVerified()
    {
        await using var context = CreateContext();
        var fixture = new SandboxFixtureService(context, NullLogger<SandboxFixtureService>.Instance);
        await fixture.RepairPickupDropoffUnicodeAsync();

        // Verify key authoritative points that previously had Mojibake
        var p1 = await context.PickupDropoffPoints.FindAsync(1L);
        Assert.NotNull(p1);
        Assert.Equal("Bến xe Miền Tây", p1.Name);
        Assert.Contains("Phường An Lạc", p1.Address);
        Assert.Contains("Bình Tân", p1.Address);

        var p5 = await context.PickupDropoffPoints.FindAsync(5L);
        Assert.NotNull(p5);
        Assert.Equal("Bưu điện TP. Đà Lạt", p5.Name);
        Assert.Contains("Đà Lạt", p5.Address);

        var p10 = await context.PickupDropoffPoints.FindAsync(10L);
        Assert.NotNull(p10);
        Assert.Equal("Bến xe Liên tỉnh Đà Lạt (FUTA)", p10.Name);
        Assert.Contains("Tô Hiến Thành", p10.Address);

        var p24 = await context.PickupDropoffPoints.FindAsync(24L);
        Assert.NotNull(p24);
        Assert.Equal("Bến xe Sa Pa", p24.Name);
        Assert.Contains("Điện Biên Phủ", p24.Address);

        var p27 = await context.PickupDropoffPoints.FindAsync(27L);
        Assert.NotNull(p27);
        Assert.Equal("Bến xe Miền Đông Cũ", p27.Name);
        Assert.Contains("Đinh Bộ Lĩnh", p27.Address);

        var p30 = await context.PickupDropoffPoints.FindAsync(30L);
        Assert.NotNull(p30);
        Assert.Equal("Bến xe Trung tâm Quy Nhơn", p30.Name);
        Assert.Contains("Ghềnh Ráng", p30.Address);
    }
}
