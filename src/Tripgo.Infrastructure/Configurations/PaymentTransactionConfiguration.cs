using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Configurations;

public class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.ToTable("PaymentTransactions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TransactionCode)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(x => x.TransactionCode)
            .IsUnique();

        builder.Property(x => x.PaymentMethod)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(x => x.Status)
            .IsRequired();

        builder.Property(x => x.ProviderReference)
            .HasMaxLength(100);

        builder.Property(x => x.Note)
            .HasMaxLength(500);

        builder.HasIndex(x => new { x.BookingId, x.Status });

        // A booking can only ever be paid once, whatever races the application layer loses.
        builder.HasIndex(x => x.BookingId)
            .IsUnique()
            .HasFilter("[Status] = 2")
            .HasDatabaseName("IX_PaymentTransactions_BookingId_Paid");

        builder.Property(x => x.Provider).HasMaxLength(50).HasDefaultValue("SANDBOX").IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).HasDefaultValue("VND").IsRequired();
        builder.Property(x => x.RefundReference).HasMaxLength(100);

        builder.HasOne(x => x.Booking)
            .WithMany(b => b.PaymentTransactions)
            .HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
