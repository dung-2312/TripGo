using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Tripgo.Application.Interfaces.Persistence;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;

namespace Tripgo.Infrastructure.Persistence.Repositories;

public sealed class TicketRepository : ITicketRepository
{
    private readonly AppDbContext _context;

    public TicketRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<OwnedBookingForTicket?> GetOwnedBookingAsync(string bookingCode, string? ownerIdentifier, long? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerIdentifier) && !userId.HasValue)
        {
            return Task.FromResult<OwnedBookingForTicket?>(null);
        }

        return _context.Bookings.AsNoTracking()
            .Where(b => b.BookingCode == bookingCode &&
                        ((userId.HasValue && b.UserId == userId.Value) ||
                         (ownerIdentifier != null && b.OwnerIdentifier == ownerIdentifier)))
            .Select(b => new OwnedBookingForTicket(
                b.Id,
                b.Status == BookingStatus.Confirmed && b.PaymentStatus == PaymentStatus.Paid,
                b.IsDemoBooking,
                b.Trip.ExpectedArrivalTime))
            .FirstOrDefaultAsync(ct);
    }

    public Task<TicketCredential?> GetLiveCredentialAsync(long bookingId, CancellationToken ct = default) =>
        _context.TicketCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.BookingId == bookingId && c.RevokedAt == null, ct);

    public async Task<bool> TryAddCredentialAsync(TicketCredential credential, CancellationToken ct = default)
    {
        _context.TicketCredentials.Add(credential);
        try
        {
            await _context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return false;
        }
        finally
        {
            _context.Entry(credential).State = EntityState.Detached;
        }
    }

    public async Task<TicketContext?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var row = await _context.TicketCredentials.AsNoTracking()
            .Where(c => c.TokenHash == tokenHash)
            .Select(c => new
            {
                Credential = c,
                c.Booking.Trip.Route.OperatorId,
                c.Booking.BookingCode,
                RouteName = c.Booking.Trip.Route.Name,
                c.Booking.Trip.DepartureTime,
                c.Booking.PassengerName,
                SeatCodes = c.Booking.BookingSeats.Select(s => s.SeatCode).ToList(),
                BookingConfirmed = c.Booking.Status == BookingStatus.Confirmed && c.Booking.PaymentStatus == PaymentStatus.Paid,
                TripCancelled = c.Booking.Trip.Status == TripStatus.Cancelled
            })
            .FirstOrDefaultAsync(ct);

        return row == null
            ? null
            : new TicketContext(row.Credential, row.OperatorId, row.BookingCode, row.RouteName, row.DepartureTime,
                row.PassengerName, row.SeatCodes, row.BookingConfirmed, row.TripCancelled);
    }

    public async Task<bool> TryMarkBoardedAsync(long credentialId, long actorUserId, DateTimeOffset now, CancellationToken ct = default)
    {
        _context.EnsureWritesTargetIsolatedTestDatabase();
        var affected = await _context.TicketCredentials
            .Where(c => c.Id == credentialId && c.BoardedAt == null && c.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.BoardedAt, now)
                .SetProperty(c => c.BoardedByUserId, actorUserId), ct);
        return affected == 1;
    }

    public async Task<int> RevokeLiveCredentialAsync(long bookingId, string reason, DateTimeOffset now, CancellationToken ct = default)
    {
        _context.EnsureWritesTargetIsolatedTestDatabase();
        return await _context.TicketCredentials
            .Where(c => c.BookingId == bookingId && c.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.RevokedAt, now)
                .SetProperty(c => c.RevokedReason, reason), ct);
    }

    public async Task AddAttemptAsync(TicketVerificationAttempt attempt, CancellationToken ct = default)
    {
        _context.TicketVerificationAttempts.Add(attempt);
        await _context.SaveChangesAsync(ct);
        _context.Entry(attempt).State = EntityState.Detached;
    }

    public Task<int> CountRecentAttemptsAsync(string actorKey, DateTimeOffset since, CancellationToken ct = default) =>
        _context.TicketVerificationAttempts.CountAsync(a => a.ActorKey == actorKey && a.AttemptedAt >= since, ct);
}
