using TripGo.Models;

namespace TripGo.Services
{
    public interface ITripSearchService
    {
        Task<List<Trip>> SearchTripsAsync(string? origin, string? destination, DateTime? departureDate);
        Task<Trip?> GetTripWithDetailsAsync(int tripId);
        Task<List<string>> GetBookedSeatsAsync(int tripId);
    }
}