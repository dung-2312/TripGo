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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Cấu hình Precision/Scale cho các trường số thực (decimal)
        modelBuilder.Entity<PickupDropoffPoint>(entity =>
        {
            entity.Property(p => p.Latitude).HasPrecision(9, 6);
            entity.Property(p => p.Longitude).HasPrecision(9, 6);
        });

        modelBuilder.Entity<Route>(entity =>
        {
            entity.Property(r => r.DistanceKm).HasPrecision(8, 2);

            // Chặn lỗi Multiple Cascade Paths khi 2 FK cùng trỏ về Locations
            entity.HasOne(r => r.OriginLocation)
                  .WithMany()
                  .HasForeignKey(r => r.OriginLocationId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.DestinationLocation)
                  .WithMany()
                  .HasForeignKey(r => r.DestinationLocationId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.Operator)
                  .WithMany()
                  .HasForeignKey(r => r.OperatorId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Trip>(entity =>
        {
            entity.Property(t => t.BasePrice).HasPrecision(18, 2);

            // Chặn cascade path từ Trip sang Route và Bus
            entity.HasOne(t => t.Route)
                  .WithMany()
                  .HasForeignKey(t => t.RouteId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(t => t.Bus)
                  .WithMany()
                  .HasForeignKey(t => t.BusId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // 2. Tự động quét và áp dụng tất cả các IEntityTypeConfiguration trong Assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}