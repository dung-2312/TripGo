using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Configurations;

public class RouteConfiguration : IEntityTypeConfiguration<Route>
{
    public void Configure(EntityTypeBuilder<Route> builder)
    {
        builder.ToTable("Routes");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.DistanceKm)
            .HasPrecision(8, 2);

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true);

        // Operator-scoped route code uniqueness
        builder.HasIndex(x => new { x.OperatorId, x.Code })
            .IsUnique();

        // Prevent accidental cascade deletes from master entities
        builder.HasOne(x => x.OriginLocation)
            .WithMany()
            .HasForeignKey(x => x.OriginLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.DestinationLocation)
            .WithMany()
            .HasForeignKey(x => x.DestinationLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Operator)
            .WithMany()
            .HasForeignKey(x => x.OperatorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_Routes_Origin_Destination_Different",
                "[OriginLocationId] <> [DestinationLocationId]");

            t.HasCheckConstraint(
                "CK_Routes_DistanceKm",
                "[DistanceKm] IS NULL OR [DistanceKm] > 0");

            t.HasCheckConstraint(
                "CK_Routes_EstimatedDurationMinutes",
                "[EstimatedDurationMinutes] IS NULL OR [EstimatedDurationMinutes] > 0");
        });
    }
}
