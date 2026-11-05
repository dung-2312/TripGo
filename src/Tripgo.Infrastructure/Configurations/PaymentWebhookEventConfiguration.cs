using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Configurations;

public sealed class PaymentWebhookEventConfiguration : IEntityTypeConfiguration<PaymentWebhookEvent>
{
    public void Configure(EntityTypeBuilder<PaymentWebhookEvent> builder)
    {
        builder.ToTable("PaymentWebhookEvents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Provider).HasMaxLength(50).IsRequired();
        builder.Property(x => x.EventId).HasMaxLength(100);
        builder.Property(x => x.TransactionCode).HasMaxLength(100);
        builder.Property(x => x.Outcome).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ReportedAmount).HasPrecision(18, 2);
        builder.Property(x => x.ReportedCurrency).HasMaxLength(3);
        builder.Property(x => x.Detail).HasMaxLength(500);
        builder.HasIndex(x => x.ReceivedAt);

        // A provider's event may be accepted once. Rejected events carry no trusted ID, so this skips them.
        builder.HasIndex(x => new { x.Provider, x.EventId })
            .IsUnique()
            .HasFilter("[EventId] IS NOT NULL AND [SignatureValid] = 1");
    }
}
