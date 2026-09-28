using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TripGo.Models;

namespace TripGo.Controllers
{
    public class HomeController : Controller
    {
        private readonly TripGoDbContext _context;

        public HomeController(TripGoDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string? origin, string? destination, DateTime? departureDate)
        {
            ViewBag.Origin = origin;
            ViewBag.Destination = destination;
            ViewBag.DepartureDate = departureDate?.ToString("yyyy-MM-dd");

            var query = _context.Trips
                .Include(t => t.Route)
                .Include(t => t.Bus)
                    .ThenInclude(b => b!.BusType)
                .AsQueryable();

            if (!string.IsNullOrEmpty(origin))
                query = query.Where(t => t.Route!.Origin.Contains(origin));

            if (!string.IsNullOrEmpty(destination))
                query = query.Where(t => t.Route!.Destination.Contains(destination));

            if (departureDate.HasValue)
                query = query.Where(t => t.DepartureTime.Date == departureDate.Value.Date);

            var trips = await query.ToListAsync();
            return View(trips);
        }

        public async Task<IActionResult> SelectSeats(int tripId)
        {
            var trip = await _context.Trips
                .Include(t => t.Route)
                .Include(t => t.Bus)
                    .ThenInclude(b => b!.BusType)
                .FirstOrDefaultAsync(t => t.TripId == tripId);

            if (trip == null) return NotFound();

            var bookedSeats = await _context.BookingSeats
                .Where(s => s.TripId == tripId)
                .Select(s => s.SeatCode)
                .ToListAsync();

            ViewBag.BookedSeats = bookedSeats;
            return View(trip);
        }
    }
}