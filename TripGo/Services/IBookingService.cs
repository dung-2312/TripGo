using TripGo.Models;

namespace TripGo.Services
{
    public interface IBookingService
    {
        Task<(bool Success, string Message, string BookingCode)> CreateBookingAsync(
            int tripId, List<string> selectedSeats, string name, string phone, string email, int? userId);
        Task<Booking?> GetBookingByCodeAsync(string bookingCode);
        Task<List<Booking>> GetBookingsByPhoneAsync(string phone);
    }
}