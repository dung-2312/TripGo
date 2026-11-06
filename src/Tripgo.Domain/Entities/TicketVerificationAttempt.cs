namespace Tripgo.Domain.Entities;

public class TicketVerificationAttempt
{
    public long Id { get; set; }
    public long? TicketCredentialId { get; set; }
    public string TokenFingerprint { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public long? ActorUserId { get; set; }
    public string ActorKey { get; set; } = string.Empty;
    public DateTimeOffset AttemptedAt { get; set; } = DateTimeOffset.UtcNow;
}
