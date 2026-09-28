using Microsoft.EntityFrameworkCore;
using TripGo.Models;

namespace TripGo.Services
{
    public class TripSearchService : ITripSearchService
    {
        private readonly TripGoDbContext _context;

        public TripSearchService(TripGoDbContext context)
        {
            _context = context;
        }

        public async Task<List<Trip>> SearchTripsAsync(string? origin, string? destination, DateTime? departureDate)
        {
            var query = _context.Trips
                .Include(t => t.Route)
                .Include(t => t.Bus)
                    .ThenInclude(b => b.BusType)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(origin))
                query = query.Where(t => t.Route.Origin.Contains(origin));

            if (!string.IsNullOrWhiteSpace(destination))
                query = query.Where(t => t.Route.Destination.Contains(destination));

            if (departureDate.HasValue)
                query = query.Where(t => t.DepartureTime.Date == departureDate.Value.Date);

            return await query.OrderBy(t => t.DepartureTime).ToListAsync();
        }

        public async Task<Trip?> GetTripWithDetailsAsync(int tripId)
        {
            return await _context.Trips
                .Include(t => t.Route)
                .Include(t => t.Bus)
                    .ThenInclude(b => b.BusType)
                .FirstOrDefaultAsync(t => t.TripId == tripId);
        }

        public async Task<List<string>> GetBookedSeatsAsync(int tripId)
        {
            return await _context.BookingSeats
                .Where(s => s.TripId == tripId)
                .Select(s => s.SeatCode)
                .ToListAsync();
        }
    }
}