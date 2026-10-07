using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Configurations;

public class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> builder)
    {
        builder.ToTable("Drivers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.FullName)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.Phone)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.LicenseNumber)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.LicenseClass)
            .HasMaxLength(20);

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true);

        // Operator-scoped uniqueness: an operator cannot register the same license number twice
        builder.HasIndex(x => new { x.OperatorId, x.LicenseNumber })
            .IsUnique();

        // Fast search by license number
        builder.HasIndex(x => x.LicenseNumber);

        // Prevent unsafe cascade deletion from Operator
        builder.HasOne(x => x.Operator)
            .WithMany()
            .HasForeignKey(x => x.OperatorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
