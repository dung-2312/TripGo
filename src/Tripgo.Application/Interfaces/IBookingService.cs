using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tripgo.Application.DTOs;

namespace Tripgo.Application.Interfaces;

public interface IBookingService
{
    Task<List<TripSeatDto>> GetSeatsForTripAsync(long tripId, CancellationToken ct = default);
    Task<SeatHoldResultDto> HoldSeatsAsync(SeatHoldRequestDto request, CancellationToken ct = default);
    Task<bool> ReleaseHoldAsync(string holdToken, CancellationToken ct = default);
    Task<BookingResultDto> CreateBookingAsync(CreateBookingRequestDto request, CancellationToken ct = default);
    Task<BookingDetailDto?> GetBookingByCodeAsync(string bookingCode, CancellationToken ct = default);
    Task<List<BookingDetailDto>> GetBookingsByPhoneAsync(string phone, CancellationToken ct = default);
    Task EnsureDemoTripInventoryAsync(long tripId, CancellationToken ct = default);
    Task<bool> CancelBookingAsync(long bookingId, string reason, CancellationToken ct = default);
    Task<List<BookingDetailDto>> GetBookingsForCurrentUserAsync(CancellationToken ct = default);
}
