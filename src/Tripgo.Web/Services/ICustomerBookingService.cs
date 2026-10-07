using Tripgo.Web.Models;

namespace Tripgo.Web.Services;

public interface ICustomerBookingService
{
    Task<List<string>> GetPopularOriginsAsync();
    Task<List<string>> GetPopularDestinationsAsync();
    Task<List<CustomerTripDto>> SearchTripsAsync(TripSearchFilter filter);
    Task<CustomerTripDto?> GetTripDetailsAsync(long tripId);
    Task<List<CustomerSeatDto>> GetSeatsForTripAsync(long tripId);
    Task<(bool Success, string Message, CustomerBookingResultDto? Result)> CreateBookingAsync(CustomerBookingRequest request);
    Task<CustomerBookingResultDto?> GetBookingResultAsync(string bookingCode);
    Task<List<CustomerBookingResultDto>> SearchBookingsAsync(string keyword);
}
