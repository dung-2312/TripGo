using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Configurations;

public class SeatLayoutConfiguration : IEntityTypeConfiguration<SeatLayout>
{
    public void Configure(EntityTypeBuilder<SeatLayout> builder)
    {
        builder.ToTable("SeatLayouts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.TotalSeats)
            .IsRequired();

        builder.Property(x => x.Floors)
            .HasDefaultValue(1);

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true);

        builder.HasMany(x => x.Items)
            .WithOne(x => x.SeatLayout)
            .HasForeignKey(x => x.SeatLayoutId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_SeatLayouts_TotalSeats",
                "[TotalSeats] > 0");

            t.HasCheckConstraint(
                "CK_SeatLayouts_Floors",
                "[Floors] > 0");
        });
    }
}