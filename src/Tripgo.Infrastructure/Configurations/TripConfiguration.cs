using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Configurations;

public class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> builder)
    {
        builder.ToTable("Trips");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TripCode)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(x => x.TripCode)
            .IsUnique();

        builder.Property(x => x.BasePrice)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(x => x.IsDemoTrip)
            .HasDefaultValue(false);

        // Exactly ONE relationship between Trip and Route with explicit inverse navigation (prevents shadow FK RouteId1)
        builder.HasOne(x => x.Route)
            .WithMany(r => r.Trips)
            .HasForeignKey(x => x.RouteId)
            .OnDelete(DeleteBehavior.Restrict);

        // Bus deletion must not destroy trip history
        builder.HasOne(x => x.Bus)
            .WithMany()
            .HasForeignKey(x => x.BusId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optimization indexes for trip search
        builder.HasIndex(x => new { x.DepartureTime, x.Status });
        builder.HasIndex(x => x.RouteId);
        builder.HasIndex(x => x.BusId);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_Trips_Arrival_After_Departure",
                "[ExpectedArrivalTime] > [DepartureTime]");

            t.HasCheckConstraint(
                "CK_Trips_BasePrice",
                "[BasePrice] >= 0");
        });
    }
}
