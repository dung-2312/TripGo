using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Configurations;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("Bookings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.BookingCode)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(x => x.BookingCode)
            .IsUnique();

        builder.Property(x => x.HoldToken)
            .HasMaxLength(100);

        builder.HasIndex(x => x.HoldToken)
            .IsUnique()
            .HasFilter("[HoldToken] IS NOT NULL");

        builder.Property(x => x.OwnerIdentifier)
            .HasMaxLength(150);

        builder.HasIndex(x => new { x.OwnerIdentifier, x.CreatedAt });
        builder.HasIndex(x => new { x.PassengerPhone, x.OwnerIdentifier });

        builder.Property(x => x.PassengerName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.PassengerPhone)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.PassengerEmail)
            .HasMaxLength(200);

        builder.Property(x => x.PickupPointNote)
            .HasMaxLength(500);

        builder.Property(x => x.DropoffPointNote)
            .HasMaxLength(500);

        builder.Property(x => x.BaseAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(x => x.SurchargeAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(x => x.InsuranceAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(x => x.TotalAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(x => x.CancellationReason)
            .HasMaxLength(500);

        builder.HasIndex(x => new { x.PassengerPhone, x.CreatedAt });
        builder.HasIndex(x => new { x.TripId, x.Status });

        builder.HasOne(x => x.Trip)
            .WithMany(t => t.Bookings)
            .HasForeignKey(x => x.TripId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.PickupPoint)
            .WithMany()
            .HasForeignKey(x => x.PickupPointId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.DropoffPoint)
            .WithMany()
            .HasForeignKey(x => x.DropoffPointId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.User)
            .WithMany(u => u.Bookings)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
