namespace Tripgo.Domain.Entities;

/// <summary>
/// A boarding credential for a confirmed booking. The raw token is never stored: lookups use its hash,
/// and the owner's copy is kept only in encrypted form. It carries no passenger information.
/// </summary>
public class TicketCredential
{
    public long Id { get; set; }
    public long BookingId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string ProtectedToken { get; set; } = string.Empty;
    public bool IsSandbox { get; set; } = true;
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public DateTimeOffset? BoardedAt { get; set; }
    public long? BoardedByUserId { get; set; }
    public Booking Booking { get; set; } = null!;
}
