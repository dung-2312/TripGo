using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Configurations;

public class OperatorConfiguration : IEntityTypeConfiguration<Operator>
{
    public void Configure(EntityTypeBuilder<Operator> builder)
    {
        builder.ToTable("Operators");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code)
            .HasMaxLength(30)
            .IsRequired();

        builder.HasIndex(x => x.Code)
            .IsUnique();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.LegalName)
            .HasMaxLength(250);

        builder.Property(x => x.TaxCode)
            .HasMaxLength(30);

        builder.HasIndex(x => x.TaxCode)
            .IsUnique()
            .HasFilter("[TaxCode] IS NOT NULL");

        builder.Property(x => x.Phone)
            .HasMaxLength(20);

        builder.Property(x => x.Email)
            .HasMaxLength(254);

        builder.Property(x => x.Address)
            .HasMaxLength(500);

        builder.Property(x => x.LogoUrl)
            .HasMaxLength(1000);

        builder.Property(x => x.Description)
            .HasMaxLength(2000);

        builder.Property(x => x.VerificationEvidence).HasMaxLength(1000);
        builder.Property(x => x.SourceReference).HasMaxLength(500);

        builder.Property(x => x.IsVerified)
            .HasDefaultValue(false);

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true);
    }
}