namespace Tripgo.Domain.Entities;

public class Operator
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? LegalName { get; set; }

    public string? TaxCode { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Address { get; set; }

    public string? LogoUrl { get; set; }

    public string? Description { get; set; }

    public bool IsVerified { get; set; }
    public string? VerificationEvidence { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public long? VerifiedByUserId { get; set; }
    public Enums.TransportDataSource DataSource { get; set; } = Enums.TransportDataSource.Unverified;
    public string? SourceReference { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
        = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}