using System.Collections.Generic;
using Tripgo.Application.DTOs;

namespace Tripgo.Application.Services.Temporary;

/// <summary>
/// TEMPORARY TECHNICAL DEBT (Phase 2):
/// In-memory storage for seat locks, customer bookings, and support callbacks.
/// This will be replaced in Phase 3 with database-backed persistence (Booking, SeatHold, TripSeat entities).
/// </summary>
public interface ITemporaryBookingStore
{
    int GetBookedSeatCount(long tripId);
    HashSet<string> GetBookedSeatSnapshot(long tripId);
    bool TryBookSeats(long tripId, List<string> internalSeatCodes, List<string> requestedCodes, CustomerBookingInfoDto customerInfo, string routeName, string operatorName, string busTypeName, string busLicensePlate, System.DateTimeOffset departureTime, decimal basePrice);
    List<CustomerBookingInfoDto> GetBookingsByPhone(string normalizedPhone);
    CustomerBookingInfoDto? GetBookingByCode(string bookingCode);
    void EnqueueSupportCallback(SupportCallbackRequestDto callback);
    void RemoveTripBookedSeats(long tripId);
}
