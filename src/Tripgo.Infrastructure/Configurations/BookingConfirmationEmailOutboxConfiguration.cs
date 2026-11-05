using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Configurations;

public sealed class BookingConfirmationEmailOutboxConfiguration
    : IEntityTypeConfiguration<BookingConfirmationEmailOutbox>
{
    public void Configure(EntityTypeBuilder<BookingConfirmationEmailOutbox> builder)
    {
        builder.ToTable("BookingConfirmationEmailOutbox");

        builder.HasKey(item => item.Id);

        builder.Property(item => item.RecipientEmail)
            .HasMaxLength(254)
            .IsRequired();

        builder.Property(item => item.Payload)
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Property(item => item.Status)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(item => item.LastError)
            .HasMaxLength(1000);

        builder.HasIndex(item => new { item.Status, item.NextAttemptAt });
        builder.HasIndex(item => item.BookingId).IsUnique();

        builder.HasOne(item => item.Booking)
            .WithMany()
            .HasForeignKey(item => item.BookingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
