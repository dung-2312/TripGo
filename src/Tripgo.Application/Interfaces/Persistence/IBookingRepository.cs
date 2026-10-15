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
    Task<Booking?> GetBookingByCodeAsync(string bookingCode, string? ownerIdentifier, long? userId = null, CancellationToken ct = default);
    Task<List<Booking>> GetBookingsByPhoneAsync(string phone, string? ownerIdentifier, long? userId = null, CancellationToken ct = default);
    Task<List<Booking>> GetBookingsByUserIdAsync(long userId, CancellationToken ct = default);
    Task<List<Booking>> GetBookingsByOwnerIdentifierAsync(string ownerIdentifier, CancellationToken ct = default);
    Task<int> ClaimGuestBookingsAsync(long userId, string guestOwnerIdentifier, CancellationToken ct = default);
    Task ExpireStaleHoldsAsync(CancellationToken ct = default);
    Task ExpireStaleBookingsAsync(CancellationToken ct = default);
    Task<bool> CancelBookingAsync(long bookingId, string? ownerIdentifier, long? userId, string reason, CancellationToken ct = default);
    Task<PaymentProcessResultDto> ConfirmPaymentAndBookingAsync(string bookingCode, string transactionCode, string paymentMethod, decimal amount, bool isSuccess, string? failureReason, string? ownerIdentifier, long? userId = null, CancellationToken ct = default);
    Task<List<PaymentTransaction>> GetPaymentTransactionsByBookingCodeAsync(string bookingCode, string? ownerIdentifier, long? userId = null, CancellationToken ct = default);
}
