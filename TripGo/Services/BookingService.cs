using Microsoft.EntityFrameworkCore;
using TripGo.Models;

namespace TripGo.Services
{
    public class BookingService : IBookingService
    {
        private readonly TripGoDbContext _context;

        public BookingService(TripGoDbContext context)
        {
            _context = context;
        }

        public async Task<(bool Success, string Message, string BookingCode)> CreateBookingAsync(
            int tripId, List<string> selectedSeats, string name, string phone, string email, int? userId)
        {
            // Sử dụng Database Transaction để khóa dữ liệu chống 2 người đặt cùng 1 ghế
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var trip = await _context.Trips.FindAsync(tripId);
                if (trip == null) return (false, "Chuyến xe không tồn tại!", string.Empty);

                // Kiểm tra xem ghế đã bị ai mua trước chưa
                var isAnySeatBooked = await _context.BookingSeats
                    .AnyAsync(s => s.TripId == tripId && selectedSeats.Contains(s.SeatCode));

                if (isAnySeatBooked)
                {
                    await transaction.RollbackAsync();
                    return (false, "Một hoặc nhiều ghế bạn vừa chọn đã có người khác đặt. Vui lòng chọn ghế khác!", string.Empty);
                }

                string code = "TG" + DateTime.Now.ToString("yyyyMMddHHmmss");
                var booking = new Booking
                {
                    BookingCode = code,
                    UserId = userId,
                    CustomerName = name,
                    CustomerPhone = phone,
                    CustomerEmail = email,
                    TotalAmount = trip.Price * selectedSeats.Count,
                    BookingStatus = "Confirmed",
                    PaymentStatus = "Paid",
                    CreatedAt = DateTime.Now
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
                await transaction.CommitAsync();

                return (true, "Đặt vé thành công!", code);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (false, "Có lỗi xảy ra: " + ex.Message, string.Empty);
            }
        }

        public async Task<Booking?> GetBookingByCodeAsync(string bookingCode)
        {
            return await _context.Bookings
                .Include(b => b.BookingSeats)
                .FirstOrDefaultAsync(b => b.BookingCode == bookingCode);
        }

        public async Task<List<Booking>> GetBookingsByPhoneAsync(string phone)
        {
            return await _context.Bookings
                .Include(b => b.BookingSeats)
                .Where(b => b.CustomerPhone == phone)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
        }
    }
}