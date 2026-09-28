using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TripGo.Models;

namespace TripGo.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class DashboardController : Controller
    {
        private readonly TripGoDbContext _context;

        public DashboardController(TripGoDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            ViewBag.TotalRevenue = await _context.Bookings
                .Where(b => b.PaymentStatus == "Paid")
                .SumAsync(b => (decimal?)b.TotalAmount) ?? 0;

            ViewBag.TotalTickets = await _context.BookingSeats.CountAsync();
            ViewBag.TotalTrips = await _context.Trips.CountAsync();
            ViewBag.TotalBuses = await _context.Buses.CountAsync();

            var recentBookings = await _context.Bookings
                .Include(b => b.BookingSeats)
                .OrderByDescending(b => b.CreatedAt)
                .Take(10)
                .ToListAsync();

            return View(recentBookings);
        }
    }
}