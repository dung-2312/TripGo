using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tripgo.Application.DTOs;
using Tripgo.Domain.Entities;

namespace Tripgo.Application.Interfaces.Persistence;

public interface IBookingRepository
{
    Task<Trip?> GetTripForBookingAsync(long tripId, CancellationToken ct = default);
    Task<List<TripSeat>> GetTripSeatsAsync(long tripId, CancellationToken ct = default);
    Task AddTripSeatsAsync(IEnumerable<TripSeat> seats, CancellationToken ct = default);
    Task<SeatHoldResultDto> HoldSeatsAsync(long tripId, List<string> seatCodes, string holdToken, int holdMinutes, string? ownerIdentifier = null, CancellationToken ct = default);
    Task<bool> ReleaseHoldAsync(string holdToken, string ownerIdentifier, CancellationToken ct = default);
    Task<BookingResultDto> CreateBookingAsync(Booking booking, List<BookingSeat> seats, List<BookingPassenger> passengers, string holdToken, string? ownerIdentifier = null, CancellationToken ct = default);
    Task<Booking?> GetBookingByCodeAsync(string bookingCode, string ownerIdentifier, CancellationToken ct = default);
    Task<List<Booking>> GetBookingsByPhoneAsync(string phone, string ownerIdentifier, CancellationToken ct = default);
    Task ExpireStaleHoldsAsync(CancellationToken ct = default);
    Task ExpireStaleBookingsAsync(CancellationToken ct = default);
    Task<bool> CancelBookingAsync(long bookingId, string ownerIdentifier, string reason, CancellationToken ct = default);
}
