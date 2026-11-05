using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Configurations;

public sealed class TicketVerificationAttemptConfiguration : IEntityTypeConfiguration<TicketVerificationAttempt>
{
    public void Configure(EntityTypeBuilder<TicketVerificationAttempt> builder)
    {
        builder.ToTable("TicketVerificationAttempts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TokenFingerprint).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Result).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ActorKey).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => new { x.ActorKey, x.AttemptedAt });
        builder.HasIndex(x => x.TicketCredentialId);
    }
}
