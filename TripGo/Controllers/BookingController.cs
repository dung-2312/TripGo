using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TripGo.Models;

namespace TripGo.Controllers
{
    public class BookingController : Controller
    {
        private readonly TripGoDbContext _context;

        public BookingController(TripGoDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> Checkout(int tripId, List<string> selectedSeats)
        {
            if (selectedSeats == null || !selectedSeats.Any())
            {
                TempData["Error"] = "Bạn chưa chọn vị trí ghế nào!";
                return RedirectToAction("SelectSeats", "Home", new { tripId });
            }

            var trip = await _context.Trips
                .Include(t => t.Route)
                .FirstOrDefaultAsync(t => t.TripId == tripId);

            if (trip == null) return NotFound();

            ViewBag.Trip = trip;
            ViewBag.SelectedSeats = selectedSeats;
            ViewBag.TotalAmount = trip.Price * selectedSeats.Count;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmBooking(int tripId, List<string> selectedSeats, string customerName, string customerPhone, string customerEmail)
        {
            using var trans = await _context.Database.BeginTransactionAsync();
            try
            {
                var isBooked = await _context.BookingSeats.AnyAsync(s => s.TripId == tripId && selectedSeats.Contains(s.SeatCode));
                if (isBooked)
                {
                    await trans.RollbackAsync();
                    TempData["Error"] = "Ghế bạn chọn vừa có người đặt. Xin vui lòng chọn ghế khác!";
                    return RedirectToAction("SelectSeats", "Home", new { tripId });
                }

                var trip = await _context.Trips.FindAsync(tripId);
                var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
                int? userId = string.IsNullOrEmpty(userIdClaim) ? null : int.Parse(userIdClaim);

                var booking = new Booking
                {
                    BookingCode = "TG" + DateTime.Now.ToString("yyyyMMddHHmmss"),
                    UserId = userId,
                    CustomerName = customerName,
                    CustomerPhone = customerPhone,
                    CustomerEmail = customerEmail,
                    TotalAmount = trip!.Price * selectedSeats.Count,
                    BookingStatus = "Confirmed",
                    PaymentStatus = "Paid"
                };

                _context.Bookings.Add(booking);
                await _context.SaveChangesAsync();

                foreach (var seat in selectedSeats)
                {
                    _context.BookingSeats.Add(new BookingSeat
                    {
                        BookingId = booking.BookingId,
                        TripId = tripId,
                        SeatCode = seat,
                        Price = trip.Price
                    });
                }

                await _context.SaveChangesAsync();
                await trans.CommitAsync();

                return RedirectToAction("Success", new { code = booking.BookingCode });
            }
            catch
            {
                await trans.RollbackAsync();
                TempData["Error"] = "Có lỗi xảy ra khi xử lý vé. Vui lòng thử lại!";
                return RedirectToAction("Index", "Home");
            }
        }

        public async Task<IActionResult> Success(string code)
        {
            var booking = await _context.Bookings
                .Include(b => b.BookingSeats)
                .FirstOrDefaultAsync(b => b.BookingCode == code);

            if (booking == null) return NotFound();
            return View(booking);
        }
    }
}