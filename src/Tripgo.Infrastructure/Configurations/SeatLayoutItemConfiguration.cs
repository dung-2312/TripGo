using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Configurations;

public class SeatLayoutItemConfiguration
    : IEntityTypeConfiguration<SeatLayoutItem>
{
    public void Configure(EntityTypeBuilder<SeatLayoutItem> builder)
    {
        builder.ToTable("SeatLayoutItems");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SeatCode)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.SeatType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Floor)
            .IsRequired();

        builder.Property(x => x.Row)
            .IsRequired();

        builder.Property(x => x.Column)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true);

        // A01 cannot appear twice in the same layout.
        builder.HasIndex(x => new
        {
            x.SeatLayoutId,
            x.SeatCode
        }).IsUnique();

        // Two seats cannot occupy the same physical position.
        builder.HasIndex(x => new
        {
            x.SeatLayoutId,
            x.Floor,
            x.Row,
            x.Column
        }).IsUnique();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_SeatLayoutItems_Floor",
                "[Floor] > 0");

            t.HasCheckConstraint(
                "CK_SeatLayoutItems_Row",
                "[Row] > 0");

            t.HasCheckConstraint(
                "CK_SeatLayoutItems_Column",
                "[Column] > 0");
        });
    }
}