using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;

namespace Tripgo.Domain;

/// <summary>The business rules for selling and cancelling, kept in one place so every path applies the same ones.</summary>
public static class BookingRules
{
    /// <summary>Sales close shortly before departure so a seat is never sold for a bus that has already left.</summary>
    public static readonly TimeSpan SalesCutoffBeforeDeparture = TimeSpan.FromMinutes(15);
    public const int MaxSeatsPerBooking = 8;
    public const int DefaultHoldMinutes = 10;
    public const int PaymentWindowMinutes = 15;

    public static bool IsSandboxTrip(Trip trip) =>
        trip.IsDemoTrip ||
        trip.TripCode.StartsWith("DEMO-", StringComparison.OrdinalIgnoreCase) ||
        trip.TripCode.StartsWith("[SANDBOX]", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns null when seats on the trip may be sold now, otherwise why they may not.</summary>
    public static string? GetSaleBlockReason(Trip trip, DateTimeOffset now)
    {
        if (trip.Status != TripStatus.Scheduled)
        {
            return "Chuyến xe này không còn mở bán.";
        }

        if (trip.DepartureTime <= now.Add(SalesCutoffBeforeDeparture))
        {
            return "Chuyến xe đã đóng bán vì sắp khởi hành hoặc đã khởi hành.";
        }

        return null;
    }

    /// <summary>A customer may cancel until money has actually been taken. A failed attempt took no money.</summary>
    public static bool CustomerCanCancel(Booking booking) =>
        booking.Status == BookingStatus.PendingPayment &&
        booking.PaymentStatus is PaymentStatus.Unpaid or PaymentStatus.Failed;

    /// <summary>An unconfirmed booking stops holding its seats when its payment window ends.</summary>
    public static bool IsPaymentWindowOver(Booking booking, DateTimeOffset now) =>
        booking.Status == BookingStatus.PendingPayment &&
        booking.PaymentStatus is PaymentStatus.Unpaid or PaymentStatus.Failed or PaymentStatus.Pending &&
        booking.ExpiresAt <= now;
}
