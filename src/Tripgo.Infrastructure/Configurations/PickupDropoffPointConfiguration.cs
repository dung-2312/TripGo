using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Configurations;

public class PickupDropoffPointConfiguration : IEntityTypeConfiguration<PickupDropoffPoint>
{
    public void Configure(EntityTypeBuilder<PickupDropoffPoint> builder)
    {
        builder.ToTable("PickupDropoffPoints");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.Address)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(x => x.Latitude)
            .HasPrecision(9, 6);

        builder.Property(x => x.Longitude)
            .HasPrecision(9, 6);

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true);

        // Prevent unsafe cascade deletion from Operator and Location
        builder.HasOne(x => x.Operator)
            .WithMany()
            .HasForeignKey(x => x.OperatorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Location)
            .WithMany()
            .HasForeignKey(x => x.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.OperatorId);
        builder.HasIndex(x => x.LocationId);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_PickupDropoffPoints_Latitude",
                "[Latitude] IS NULL OR ([Latitude] >= -90.0 AND [Latitude] <= 90.0)");

            t.HasCheckConstraint(
                "CK_PickupDropoffPoints_Longitude",
                "[Longitude] IS NULL OR ([Longitude] >= -180.0 AND [Longitude] <= 180.0)");
        });
    }
}
