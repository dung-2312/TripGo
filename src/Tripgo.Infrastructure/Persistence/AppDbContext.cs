using Microsoft.EntityFrameworkCore;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    // 1. Phân quyền & Người dùng
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();

    // 2. Nhà xe & Phương tiện
    public DbSet<Operator> Operators => Set<Operator>();
    public DbSet<BusType> BusTypes => Set<BusType>();
    public DbSet<SeatLayout> SeatLayouts => Set<SeatLayout>();
    public DbSet<SeatLayoutItem> SeatLayoutItems => Set<SeatLayoutItem>();
    public DbSet<Bus> Buses => Set<Bus>();
    public DbSet<Driver> Drivers => Set<Driver>();

    // 3. Địa điểm & Tuyến đường
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<PickupDropoffPoint> PickupDropoffPoints => Set<PickupDropoffPoint>();
    public DbSet<Route> Routes => Set<Route>();
    public DbSet<RouteStop> RouteStops => Set<RouteStop>();

    // 4. Chuyến xe & Lịch trình
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<TripDriver> TripDrivers => Set<TripDriver>();
    public DbSet<TripStop> TripStops => Set<TripStop>();

    // 5. Quản lý Đặt vé & Giữ chỗ (Phase 3A Persistent Booking Core)
    public DbSet<TripSeat> TripSeats => Set<TripSeat>();
    public DbSet<SeatHold> SeatHolds => Set<SeatHold>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingSeat> BookingSeats => Set<BookingSeat>();
    public DbSet<BookingPassenger> BookingPassengers => Set<BookingPassenger>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tự động quét và áp dụng tất cả các IEntityTypeConfiguration trong Assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}