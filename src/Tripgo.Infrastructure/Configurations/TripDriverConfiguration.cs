using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Configurations;

public class TripDriverConfiguration : IEntityTypeConfiguration<TripDriver>
{
    public void Configure(EntityTypeBuilder<TripDriver> builder)
    {
        builder.ToTable("TripDrivers");

        // Surrogate primary key as defined on the entity
        builder.HasKey(x => x.Id);

        // Prevent duplicate assignment of the same driver to the same trip
        builder.HasIndex(x => new { x.TripId, x.DriverId })
            .IsUnique();

        builder.HasOne(x => x.Trip)
            .WithMany(t => t.Drivers)
            .HasForeignKey(x => x.TripId)
            .OnDelete(DeleteBehavior.Cascade);

        // Driver deletion must not cascade delete assignment history
        builder.HasOne(x => x.Driver)
            .WithMany(d => d.TripAssignments)
            .HasForeignKey(x => x.DriverId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
