using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Tripgo.Application.DTOs;

namespace Tripgo.Application.Services.Temporary;

/// <summary>
/// TEMPORARY TECHNICAL DEBT (Phase 2):
/// Thread-safe in-memory store isolating temporary booking and seat locking behavior.
/// Preserves exact existing UI behavior until Phase 3 booking persistence is implemented.
/// </summary>
public class InMemoryTemporaryBookingStore : ITemporaryBookingStore
{
    private static readonly ConcurrentDictionary<long, HashSet<string>> _bookedSeatsMemory = new();
    private static readonly ConcurrentQueue<CustomerBookingInfoDto> _bookingsMemory = new();
    private static readonly ConcurrentQueue<SupportCallbackRequestDto> _supportCallbacksMemory = new();

    public int GetBookedSeatCount(long tripId)
    {
        return _bookedSeatsMemory.TryGetValue(tripId, out var seats)
            ? GetSeatSnapshotCount(seats)
            : 0;
    }

    public HashSet<string> GetBookedSeatSnapshot(long tripId)
    {
        if (!_bookedSeatsMemory.TryGetValue(tripId, out var seats))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        lock (seats)
            return new HashSet<string>(seats, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryBookSeats(
        long tripId,
        List<string> internalSeatCodes,
        List<string> requestedCodes,
        CustomerBookingInfoDto customerInfo,
        string routeName,
        string operatorName,
        string busTypeName,
        string busLicensePlate,
        DateTimeOffset departureTime,
        decimal basePrice)
    {
        var currentBooked = _bookedSeatsMemory.GetOrAdd(tripId, _ => new HashSet<string>());

        lock (currentBooked)
        {
            if (internalSeatCodes.Any(currentBooked.Contains))
            {
                return false;
            }

            foreach (var code in internalSeatCodes)
            {
                currentBooked.Add(code);
            }

            var booking = CloneBooking(customerInfo);
            booking.TripId = tripId;
            booking.BookingCode = $"TRIPGO-{Convert.ToHexString(RandomNumberGenerator.GetBytes(3))[..5]}";
            booking.SeatCodes = requestedCodes;
            booking.RouteName = routeName;
            booking.OperatorName = operatorName;
            booking.BusTypeName = busTypeName;
            booking.BusLicensePlate = busLicensePlate;
            booking.DepartureTime = departureTime;
            booking.BasePrice = basePrice;
            booking.BookedAt = DateTimeOffset.UtcNow;
            booking.InsuranceAmount = booking.HasInsurance ? requestedCodes.Count * 20_000m : 0m;
            customerInfo.BookingCode = booking.BookingCode;
            _bookingsMemory.Enqueue(booking);
        }

        return true;
    }

    public List<CustomerBookingInfoDto> GetBookingsByPhone(string normalizedPhone)
    {
        if (string.IsNullOrEmpty(normalizedPhone))
            return new List<CustomerBookingInfoDto>();

        return _bookingsMemory
            .Where(booking => NormalizePhone(booking.Phone) == normalizedPhone)
            .OrderByDescending(booking => booking.BookedAt)
            .Select(CloneBooking)
            .ToList();
    }

    public CustomerBookingInfoDto? GetBookingByCode(string bookingCode)
    {
        var booking = _bookingsMemory.FirstOrDefault(item =>
            string.Equals(item.BookingCode, bookingCode?.Trim(), StringComparison.OrdinalIgnoreCase));
        return booking == null ? null : CloneBooking(booking);
    }

    public void EnqueueSupportCallback(SupportCallbackRequestDto callback)
    {
        _supportCallbacksMemory.Enqueue(callback);
    }

    public void RemoveTripBookedSeats(long tripId)
    {
        _bookedSeatsMemory.TryRemove(tripId, out _);
    }

    private static int GetSeatSnapshotCount(HashSet<string> seats)
    {
        lock (seats)
            return seats.Count;
    }

    private static string NormalizePhone(string? phone) =>
        new((phone ?? string.Empty).Where(char.IsDigit).ToArray());

    private static CustomerBookingInfoDto CloneBooking(CustomerBookingInfoDto booking) => new()
    {
        TripId = booking.TripId,
        BookingCode = booking.BookingCode,
        Name = booking.Name,
        Phone = booking.Phone,
        Email = booking.Email,
        PickupPoint = booking.PickupPoint,
        SelectedPickupPoint = booking.SelectedPickupPoint,
        SelectedDropoffPoint = booking.SelectedDropoffPoint,
        HasInsurance = booking.HasInsurance,
        SeatCodes = new List<string>(booking.SeatCodes),
        RouteName = booking.RouteName,
        OperatorName = booking.OperatorName,
        BusTypeName = booking.BusTypeName,
        BusLicensePlate = booking.BusLicensePlate,
        DepartureTime = booking.DepartureTime,
        BookedAt = booking.BookedAt,
        BasePrice = booking.BasePrice,
        InsuranceAmount = booking.InsuranceAmount
    };
}
