namespace Tripgo.Domain.Entities;

public class Driver
{
    public long Id { get; set; }

    public long OperatorId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string LicenseNumber { get; set; } = string.Empty;

    public string? LicenseClass { get; set; }

    public DateOnly? LicenseExpiryDate { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
        = DateTimeOffset.UtcNow;

    public Operator Operator { get; set; } = null!;

    public ICollection<TripDriver> TripAssignments { get; set; }
        = new List<TripDriver>();
}