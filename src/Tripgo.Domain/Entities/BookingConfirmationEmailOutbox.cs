namespace Tripgo.Domain.Entities;

public sealed class BookingConfirmationEmailOutbox
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Sent = "Sent";
    public const string Failed = "Failed";

    public const string TypeBookingConfirmation = "BookingConfirmation";
    public const string TypeBookingAcknowledgement = "BookingAcknowledgement";
    public const string TypePaymentFailed = "PaymentFailed";
    public const string TypeBookingCancellation = "BookingCancellation";
    public const string TypeTicketNotification = "TicketNotification";

    public long Id { get; set; }
    public long BookingId { get; set; }
    public string RecipientEmail { get; set; } = string.Empty;
    public string MessageType { get; set; } = TypeBookingConfirmation;
    public string DedupKey { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string Status { get; set; } = Pending;
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Booking Booking { get; set; } = null!;
}
