using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Configurations;

public class TripSeatConfiguration : IEntityTypeConfiguration<TripSeat>
{
    public void Configure(EntityTypeBuilder<TripSeat> builder)
    {
        builder.ToTable("TripSeats");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SeatCode)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.SeatType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Price)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(x => x.Status)
            .IsRequired();

        builder.Property(x => x.RowVersion)
            .IsRowVersion();

        // Unique constraint: No duplicate seats for the same trip
        builder.HasIndex(x => new { x.TripId, x.SeatCode })
            .IsUnique();

        builder.HasIndex(x => new { x.TripId, x.Status });

        builder.HasOne(x => x.Trip)
            .WithMany(t => t.Seats)
            .HasForeignKey(x => x.TripId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.SeatLayoutItem)
            .WithMany()
            .HasForeignKey(x => x.SeatLayoutItemId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
