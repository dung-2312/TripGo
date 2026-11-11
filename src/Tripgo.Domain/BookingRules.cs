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

    /// <summary>
    /// A customer may cancel an unpaid pending booking (free cancellation),
    /// or a confirmed paid booking before departure (subject to a 50% refund policy).
    /// Completed or already cancelled bookings cannot be cancelled.
    /// </summary>
    public static bool CustomerCanCancel(Booking booking)
    {
        if (booking.Status == BookingStatus.PendingPayment &&
            booking.PaymentStatus is PaymentStatus.Unpaid or PaymentStatus.Failed)
        {
            return true;
        }

        if (booking.Status == BookingStatus.Confirmed &&
            booking.PaymentStatus == PaymentStatus.Paid)
        {
            if (booking.Trip != null && booking.Trip.DepartureTime <= DateTimeOffset.UtcNow)
            {
                return false;
            }
            return true;
        }

        return false;
    }

    /// <summary>Calculates the 50% refund amount when a paid confirmed booking is cancelled.</summary>
    public static decimal CalculateCancellationRefund(Booking booking)
    {
        if (booking.PaymentStatus == PaymentStatus.Paid && booking.TotalAmount > 0)
        {
            return Math.Round(booking.TotalAmount * 0.5m, 0);
        }
        return 0m;
    }

    /// <summary>Calculates the 50% cancellation fee when a paid confirmed booking is cancelled.</summary>
    public static decimal CalculateCancellationFee(Booking booking)
    {
        if (booking.PaymentStatus == PaymentStatus.Paid && booking.TotalAmount > 0)
        {
            return booking.TotalAmount - CalculateCancellationRefund(booking);
        }
        return 0m;
    }

    /// <summary>An unconfirmed booking stops holding its seats when its payment window ends.</summary>
    public static bool IsPaymentWindowOver(Booking booking, DateTimeOffset now) =>
        booking.Status == BookingStatus.PendingPayment &&
        booking.PaymentStatus is PaymentStatus.Unpaid or PaymentStatus.Failed or PaymentStatus.Pending &&
        booking.ExpiresAt <= now;
}
