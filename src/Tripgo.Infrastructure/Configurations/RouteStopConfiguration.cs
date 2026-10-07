using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Configurations;

public class RouteStopConfiguration : IEntityTypeConfiguration<RouteStop>
{
    public void Configure(EntityTypeBuilder<RouteStop> builder)
    {
        builder.ToTable("RouteStops");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.StopOrder)
            .IsRequired();

        // Unique stop sequence within a route
        builder.HasIndex(x => new { x.RouteId, x.StopOrder })
            .IsUnique();

        builder.HasOne(x => x.Route)
            .WithMany(r => r.Stops)
            .HasForeignKey(x => x.RouteId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a PickupDropoffPoint must not destroy route definitions
        builder.HasOne(x => x.Point)
            .WithMany()
            .HasForeignKey(x => x.PointId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_RouteStops_StopOrder",
                "[StopOrder] > 0");
        });
    }
}
