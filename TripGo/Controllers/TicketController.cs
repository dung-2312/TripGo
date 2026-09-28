using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TripGo.Models;

namespace TripGo.Controllers
{
    public class TicketController : Controller
    {
        private readonly TripGoDbContext _context;

        public TicketController(TripGoDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return View(new List<Booking>());

            var bookings = await _context.Bookings
                .Include(b => b.BookingSeats)
                .Where(b => b.BookingCode == keyword.Trim() || b.CustomerPhone == keyword.Trim())
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            ViewBag.Keyword = keyword;
            return View(bookings);
        }
    }
}