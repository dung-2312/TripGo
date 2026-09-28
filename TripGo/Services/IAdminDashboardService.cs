using TripGo.Models;

namespace TripGo.Services
{
    public interface IAdminDashboardService
    {
        Task<decimal> GetTotalRevenueAsync();
        Task<int> GetTotalTicketsSoldAsync();
        Task<int> GetTotalTripsAsync();
        Task<int> GetTotalBusesAsync();
        Task<List<Booking>> GetRecentBookingsAsync(int count);
    }
}