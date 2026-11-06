using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Configurations;

public sealed class TicketCredentialConfiguration : IEntityTypeConfiguration<TicketCredential>
{
    public void Configure(EntityTypeBuilder<TicketCredential> builder)
    {
        builder.ToTable("TicketCredentials");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ProtectedToken).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.RevokedReason).HasMaxLength(200);
        builder.HasIndex(x => x.TokenHash).IsUnique();

        // At most one live credential per booking.
        builder.HasIndex(x => x.BookingId).IsUnique().HasFilter("[RevokedAt] IS NULL");

        builder.HasOne(x => x.Booking)
            .WithMany()
            .HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
