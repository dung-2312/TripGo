using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Configurations;

public class TripStopConfiguration : IEntityTypeConfiguration<TripStop>
{
    public void Configure(EntityTypeBuilder<TripStop> builder)
    {
        builder.ToTable("TripStops");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.StopOrder)
            .IsRequired();

        // Unique stop sequence for a scheduled trip
        builder.HasIndex(x => new { x.TripId, x.StopOrder })
            .IsUnique();

        builder.HasOne(x => x.Trip)
            .WithMany(t => t.Stops)
            .HasForeignKey(x => x.TripId)
            .OnDelete(DeleteBehavior.Cascade);

        // PickupDropoffPoint deletion must not destroy trip history
        builder.HasOne(x => x.Point)
            .WithMany()
            .HasForeignKey(x => x.PointId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_TripStops_StopOrder",
                "[StopOrder] > 0");
        });
    }
}
