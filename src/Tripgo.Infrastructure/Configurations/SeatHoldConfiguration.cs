using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Configurations;

public class SeatHoldConfiguration : IEntityTypeConfiguration<SeatHold>
{
    public void Configure(EntityTypeBuilder<SeatHold> builder)
    {
        builder.ToTable("SeatHolds");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.HoldToken)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.OwnerIdentifier)
            .HasMaxLength(150);

        builder.Property(x => x.Status)
            .IsRequired();

        builder.HasIndex(x => new { x.TripSeatId, x.Status, x.ExpiresAt });

        builder.HasIndex(x => new { x.HoldToken, x.Status });

        builder.HasIndex(x => new { x.HoldToken, x.OwnerIdentifier });

        builder.HasOne(x => x.TripSeat)
            .WithMany(s => s.SeatHolds)
            .HasForeignKey(x => x.TripSeatId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
