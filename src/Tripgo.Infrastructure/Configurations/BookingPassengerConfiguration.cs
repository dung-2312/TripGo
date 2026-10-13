using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Configurations;

public class BookingPassengerConfiguration : IEntityTypeConfiguration<BookingPassenger>
{
    public void Configure(EntityTypeBuilder<BookingPassenger> builder)
    {
        builder.ToTable("BookingPassengers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.FullName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.PhoneNumber)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Email)
            .HasMaxLength(200);

        builder.Property(x => x.IdentityCard)
            .HasMaxLength(50);

        builder.HasOne(x => x.Booking)
            .WithMany(b => b.Passengers)
            .HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
