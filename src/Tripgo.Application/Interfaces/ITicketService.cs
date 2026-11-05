namespace Tripgo.Application.Interfaces;

public enum TicketVerificationResult
{
    Valid,
    Boarded,
    AlreadyBoarded,
    InvalidToken,
    Revoked,
    Expired,
    BookingNotConfirmed,
    TripCancelled,
    WrongOperator,
    RateLimited
}

public sealed class MyTicketDto
{
    public bool Available { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Token { get; init; }
    public bool IsSandbox { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? BoardedAt { get; init; }
    public DateTimeOffset? RevokedAt { get; init; }
}

/// <summary>What staff see at the door. Deliberately limited: the seats and trip, never contact details.</summary>
public sealed class TicketVerificationDto
{
    public TicketVerificationResult Result { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool IsSandbox { get; init; }
    public string? BookingCode { get; init; }
    public string? RouteName { get; init; }
    public DateTimeOffset? DepartureTime { get; init; }
    public List<string> SeatCodes { get; init; } = new();
    public string? MaskedPassengerName { get; init; }
    public DateTimeOffset? BoardedAt { get; init; }
}

public interface ITicketService
{
    /// <summary>Owner only. Issues the credential on first request once the booking is confirmed and paid.</summary>
    Task<MyTicketDto> GetMyTicketAsync(string bookingCode, CancellationToken ct = default);

    /// <summary>Admin or Operator. Checks a token without changing anything.</summary>
    Task<TicketVerificationDto> VerifyAsync(string token, CancellationToken ct = default);

    /// <summary>Admin or Operator. Marks the ticket used; only the first call for a ticket can succeed.</summary>
    Task<TicketVerificationDto> BoardAsync(string token, CancellationToken ct = default);

    /// <summary>Admin only.</summary>
    Task<bool> RevokeAsync(long bookingId, string reason, CancellationToken ct = default);
}

/// <summary>Encrypts the owner's copy of a token at rest. Implemented with ASP.NET Core Data Protection.</summary>
public interface ITicketTokenProtector
{
    string Protect(string token);
    string? Unprotect(string protectedToken);
}
