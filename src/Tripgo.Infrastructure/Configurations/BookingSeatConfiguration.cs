using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Configurations;

public class BookingSeatConfiguration : IEntityTypeConfiguration<BookingSeat>
{
    public void Configure(EntityTypeBuilder<BookingSeat> builder)
    {
        builder.ToTable("BookingSeats");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SeatCode)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Price)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.HasIndex(x => new { x.BookingId, x.TripSeatId })
            .IsUnique();

        // Database-enforced rule: Each TripSeat can belong to at most one active (non-cancelled) booking
        builder.HasIndex(x => x.TripSeatId)
            .IsUnique()
            .HasFilter("[IsActive] = 1");

        builder.HasOne(x => x.Booking)
            .WithMany(b => b.BookingSeats)
            .HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.TripSeat)
            .WithMany(s => s.BookingSeats)
            .HasForeignKey(x => x.TripSeatId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
