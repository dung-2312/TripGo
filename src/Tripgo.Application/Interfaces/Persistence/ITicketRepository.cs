using Tripgo.Domain.Entities;

namespace Tripgo.Application.Interfaces.Persistence;

public sealed record TicketContext(
    TicketCredential Credential,
    long OperatorId,
    string BookingCode,
    string RouteName,
    DateTimeOffset DepartureTime,
    string PassengerName,
    List<string> SeatCodes,
    bool BookingConfirmed,
    bool TripCancelled);

public sealed record OwnedBookingForTicket(long BookingId, bool IsConfirmedAndPaid, bool IsSandbox, DateTimeOffset ExpectedArrival);

public interface ITicketRepository
{
    Task<OwnedBookingForTicket?> GetOwnedBookingAsync(string bookingCode, string? ownerIdentifier, long? userId, CancellationToken ct = default);
    Task<TicketCredential?> GetLiveCredentialAsync(long bookingId, CancellationToken ct = default);
    /// <summary>Returns false when another request issued the credential first.</summary>
    Task<bool> TryAddCredentialAsync(TicketCredential credential, CancellationToken ct = default);
    Task<TicketContext?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    /// <summary>Atomic: returns true for exactly one caller, however many arrive at once.</summary>
    Task<bool> TryMarkBoardedAsync(long credentialId, long actorUserId, DateTimeOffset now, CancellationToken ct = default);
    Task<int> RevokeLiveCredentialAsync(long bookingId, string reason, DateTimeOffset now, CancellationToken ct = default);
    Task AddAttemptAsync(TicketVerificationAttempt attempt, CancellationToken ct = default);
    Task<int> CountRecentAttemptsAsync(string actorKey, DateTimeOffset since, CancellationToken ct = default);
}
