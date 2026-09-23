using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Configurations;

public class BusConfiguration : IEntityTypeConfiguration<Bus>
{
    public void Configure(EntityTypeBuilder<Bus> builder)
    {
        builder.ToTable("Buses");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.LicensePlate)
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(x => x.LicensePlate)
            .IsUnique();

        builder.Property(x => x.Name)
            .HasMaxLength(150);

        builder.Property(x => x.Model)
            .HasMaxLength(100);

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true);

        builder.HasOne(x => x.Operator)
            .WithMany()
            .HasForeignKey(x => x.OperatorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.BusType)
            .WithMany()
            .HasForeignKey(x => x.BusTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.SeatLayout)
            .WithMany()
            .HasForeignKey(x => x.SeatLayoutId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_Buses_ManufactureYear",
                "[ManufactureYear] IS NULL OR [ManufactureYear] BETWEEN 1990 AND 2100");
        });
    }
}