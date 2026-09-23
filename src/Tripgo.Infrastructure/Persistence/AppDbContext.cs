using Microsoft.EntityFrameworkCore;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(
        DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<Operator> Operators => Set<Operator>();

    public DbSet<BusType> BusTypes => Set<BusType>();

    public DbSet<SeatLayout> SeatLayouts => Set<SeatLayout>();

    public DbSet<SeatLayoutItem> SeatLayoutItems =>
        Set<SeatLayoutItem>();

    public DbSet<Bus> Buses => Set<Bus>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly);
    }
}