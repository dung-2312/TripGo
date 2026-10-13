using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Update;
using Tripgo.Application.DTOs;
using Tripgo.Application.Interfaces.Persistence;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;

namespace Tripgo.Infrastructure.Persistence.Repositories;

public class BookingRepository : IBookingRepository
{
    private readonly AppDbContext _context;

    public BookingRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Trip?> GetTripForBookingAsync(long tripId, CancellationToken ct = default)
    {
        return await _context.Trips
            .AsNoTracking()
            .Include(t => t.Route)
                .ThenInclude(r => r.Operator)
            .Include(t => t.Route)
                .ThenInclude(r => r.OriginLocation)
            .Include(t => t.Route)
                .ThenInclude(r => r.DestinationLocation)
            .Include(t => t.Route)
                .ThenInclude(r => r.Stops)
                    .ThenInclude(s => s.Point)
            .Include(t => t.Bus)
                .ThenInclude(b => b.BusType)
            .Include(t => t.Bus)
                .ThenInclude(b => b.SeatLayout)
                    .ThenInclude(l => l.Items)
            .FirstOrDefaultAsync(t => t.Id == tripId, ct);
    }

    public async Task<List<TripSeat>> GetTripSeatsAsync(long tripId, CancellationToken ct = default)
    {
        await ExpireStaleHoldsAsync(ct);
        await ExpireStaleBookingsAsync(ct);

        return await _context.TripSeats
            .AsNoTracking()
            .Where(s => s.TripId == tripId)
            .OrderBy(s => s.Floor)
            .ThenBy(s => s.Row)
            .ThenBy(s => s.Column)
            .ToListAsync(ct);
    }

    public async Task AddTripSeatsAsync(IEnumerable<TripSeat> seats, CancellationToken ct = default)
    {
        var inventory = seats.ToList();
        if (inventory.Count == 0) return;

        var tripIds = inventory.Select(seat => seat.TripId).Distinct().ToList();
        if (tripIds.Count != 1)
            throw new ArgumentException("Seat inventory must belong to one trip.", nameof(seats));

        var tripId = tripIds[0];
        var lockResource = $"TripSeatInventory:{tripId}";
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"DECLARE @lockResult int; EXEC @lockResult = sp_getapplock @Resource = {lockResource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000; IF @lockResult < 0 THROW 51000, 'Could not acquire the trip seat inventory lock.', 1;",
                ct);

            if (!await _context.TripSeats.AnyAsync(seat => seat.TripId == tripId, ct))
            {
                await _context.TripSeats.AddRangeAsync(inventory, ct);
                await _context.SaveChangesAsync(ct);
            }

            await transaction.CommitAsync(ct);
        });
    }

    public async Task<SeatHoldResultDto> HoldSeatsAsync(
        long tripId,
        List<string> seatCodes,
        string holdToken,
        int holdMinutes,
        string? ownerIdentifier = null,
        CancellationToken ct = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                var now = DateTimeOffset.UtcNow;
                var expiresAt = now.AddMinutes(holdMinutes);
                var distinctCodes = seatCodes.Distinct().OrderBy(c => c).ToList();
                var resolvedOwner = ownerIdentifier?.Trim();
                if (string.IsNullOrWhiteSpace(resolvedOwner))
                {
                    await transaction.RollbackAsync(ct);
                    return new SeatHoldResultDto
                    {
                        Success = false,
                        Message = "Phiên đặt chỗ không hợp lệ."
                    };
                }

                var tokenBelongsToAnotherOwner = await _context.SeatHolds
                    .AnyAsync(hold => hold.HoldToken == holdToken &&
                                      (hold.OwnerIdentifier == null ||
                                       hold.OwnerIdentifier != resolvedOwner), ct);
                if (tokenBelongsToAnotherOwner)
                {
                    await transaction.RollbackAsync(ct);
                    return new SeatHoldResultDto
                    {
                        Success = false,
                        Message = "Mã giữ chỗ đã được gắn với một phiên khác."
                    };
                }

                // 1. Expire stale holds for this trip
                var staleHolds = await _context.SeatHolds
                    .Include(h => h.TripSeat)
                    .Where(h => h.TripSeat.TripId == tripId &&
                                h.Status == SeatHoldStatus.Active &&
                                h.ExpiresAt <= now)
                    .ToListAsync(ct);

                foreach (var stale in staleHolds)
                {
                    stale.Status = SeatHoldStatus.Expired;
                    if (stale.TripSeat.Status == TripSeatStatus.Held)
                    {
                        stale.TripSeat.Status = TripSeatStatus.Available;
                        stale.TripSeat.UpdatedAt = now;
                    }
                }

                // 2. Query target seats with deterministic ordering to prevent deadlocks
                var targetSeats = await _context.TripSeats
                    .Include(s => s.SeatHolds)
                    .Include(s => s.BookingSeats)
                        .ThenInclude(bs => bs.Booking)
                    .Where(s => s.TripId == tripId && distinctCodes.Contains(s.SeatCode))
                    .OrderBy(s => s.Id)
                    .ToListAsync(ct);

                if (targetSeats.Count != distinctCodes.Count)
                {
                    var missing = distinctCodes.Except(targetSeats.Select(s => s.SeatCode)).ToList();
                    await transaction.RollbackAsync(ct);
                    return new SeatHoldResultDto
                    {
                        Success = false,
                        Message = $"Không tìm thấy các ghế: {string.Join(", ", missing)}"
                    };
                }

                // 3. Verify availability of each seat
                foreach (var seat in targetSeats)
                {
                    var hasActiveBooking = seat.BookingSeats.Any(bs =>
                        bs.IsActive &&
                        (bs.Booking.Status == BookingStatus.PendingPayment ||
                         bs.Booking.Status == BookingStatus.Confirmed));

                    if (seat.Status == TripSeatStatus.Booked || hasActiveBooking)
                    {
                        await transaction.RollbackAsync(ct);
                        return new SeatHoldResultDto
                        {
                            Success = false,
                            Message = $"Ghế {seat.SeatCode} đã được đặt bởi hành khách khác."
                        };
                    }

                    var activeHold = seat.SeatHolds.FirstOrDefault(h =>
                        h.Status == SeatHoldStatus.Active &&
                        h.ExpiresAt > now &&
                        (!string.Equals(h.HoldToken, holdToken, StringComparison.OrdinalIgnoreCase) ||
                         !string.Equals(h.OwnerIdentifier, ownerIdentifier?.Trim(), StringComparison.Ordinal)));

                    if (activeHold != null)
                    {
                        await transaction.RollbackAsync(ct);
                        return new SeatHoldResultDto
                        {
                            Success = false,
                            Message = $"Ghế {seat.SeatCode} đang được giữ chỗ bởi hành khách khác."
                        };
                    }
                }

                // 4. Create or extend holds
                var newlyHeldCodes = new List<string>();
                decimal totalPrice = 0m;

                foreach (var seat in targetSeats)
                {
                    var existingTokenHold = seat.SeatHolds.FirstOrDefault(h =>
                        string.Equals(h.HoldToken, holdToken, StringComparison.OrdinalIgnoreCase) &&
                        h.Status == SeatHoldStatus.Active);

                    if (existingTokenHold != null)
                    {
                        existingTokenHold.ExpiresAt = expiresAt;
                        existingTokenHold.OwnerIdentifier = resolvedOwner;
                    }
                    else
                    {
                        _context.SeatHolds.Add(new SeatHold
                        {
                            TripSeatId = seat.Id,
                            HoldToken = holdToken,
                            OwnerIdentifier = resolvedOwner,
                            HeldAt = now,
                            ExpiresAt = expiresAt,
                            Status = SeatHoldStatus.Active
                        });
                    }

                    seat.Status = TripSeatStatus.Held;
                    seat.UpdatedAt = now;
                    newlyHeldCodes.Add(seat.SeatCode);
                    totalPrice += seat.Price;
                }

                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return new SeatHoldResultDto
                {
                    Success = true,
                    Message = "Giữ chỗ thành công trong 10 phút.",
                    HoldToken = holdToken,
                    ExpiresAt = expiresAt,
                    HeldSeatCodes = newlyHeldCodes,
                    TotalPrice = totalPrice
                };
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync(ct);
                return new SeatHoldResultDto
                {
                    Success = false,
                    Message = "Ghế vừa được thay đổi bởi một yêu cầu khác. Vui lòng tải lại và chọn lại."
                };
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    public async Task<bool> ReleaseHoldAsync(string holdToken, string ownerIdentifier, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerIdentifier)) return false;
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var holds = await _context.SeatHolds
                .Include(h => h.TripSeat)
                .Where(h => h.HoldToken == holdToken &&
                            h.OwnerIdentifier == ownerIdentifier &&
                            h.Status == SeatHoldStatus.Active)
                .ToListAsync(ct);

            if (holds.Count == 0)
            {
                await transaction.RollbackAsync(ct);
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var hold in holds)
            {
                hold.Status = SeatHoldStatus.Released;
                if (hold.TripSeat != null && hold.TripSeat.Status == TripSeatStatus.Held)
                {
                    hold.TripSeat.Status = TripSeatStatus.Available;
                    hold.TripSeat.UpdatedAt = now;
                }
            }

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        });
    }

    public async Task<BookingResultDto> CreateBookingAsync(
        Booking booking,
        List<BookingSeat> seats,
        List<BookingPassenger> passengers,
        string holdToken,
        string? ownerIdentifier = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerIdentifier))
            return new BookingResultDto { Success = false, Message = "Phiên đặt chỗ không hợp lệ." };

        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                var now = DateTimeOffset.UtcNow;
                var seatIds = seats.Select(s => s.TripSeatId).Distinct().OrderBy(id => id).ToList();

                // 0. Prevent replay attacks using identical HoldToken
                var isTokenAlreadyUsed = await _context.Bookings
                    .AnyAsync(b => b.HoldToken == holdToken, ct);

                if (isTokenAlreadyUsed)
                {
                    await transaction.RollbackAsync(ct);
                    return new BookingResultDto
                    {
                        Success = false,
                        Message = "Mã phiên giữ chỗ này đã được sử dụng để tạo đơn đặt chỗ trước đó."
                    };
                }

                // 1. Verify that all requested seats have valid, unexpired holds for THIS token AND belong to this trip
                var validHolds = await _context.SeatHolds
                    .Include(h => h.TripSeat)
                    .Where(h => seatIds.Contains(h.TripSeatId) &&
                                h.HoldToken == holdToken &&
                                h.OwnerIdentifier == ownerIdentifier &&
                                h.TripSeat.TripId == booking.TripId &&
                                h.Status == SeatHoldStatus.Active &&
                                h.ExpiresAt > now)
                    .OrderBy(h => h.TripSeatId)
                    .ToListAsync(ct);

                if (validHolds.Count != seatIds.Count)
                {
                    await transaction.RollbackAsync(ct);
                    return new BookingResultDto
                    {
                        Success = false,
                        Message = "Phiên giữ chỗ của bạn đã hết hạn hoặc không hợp lệ. Vui lòng chọn lại ghế."
                    };
                }

                // 2. Database-backed guard: Verify none of these seats already have an active BookingSeat
                var activeBookingConflict = await _context.BookingSeats
                    .Where(bs => seatIds.Contains(bs.TripSeatId) && bs.IsActive)
                    .Select(bs => bs.SeatCode)
                    .ToListAsync(ct);

                if (activeBookingConflict.Count > 0)
                {
                    await transaction.RollbackAsync(ct);
                    return new BookingResultDto
                    {
                        Success = false,
                        Message = $"Các ghế sau đã thuộc đơn đặt chỗ hoạt động khác: {string.Join(", ", activeBookingConflict)}"
                    };
                }

                // 3. Add booking entity with associated HoldToken
                if (booking.Status != BookingStatus.PendingPayment ||
                    booking.PaymentStatus != PaymentStatus.Unpaid)
                {
                    await transaction.RollbackAsync(ct);
                    return new BookingResultDto
                    {
                        Success = false,
                        Message = "Đơn mới chỉ được tạo ở trạng thái chờ thanh toán."
                    };
                }

                booking.HoldToken = holdToken;
                _context.Bookings.Add(booking);
                await _context.SaveChangesAsync(ct);

                // 4. Link booking to seats (IsActive = true) and passengers
                foreach (var seat in seats)
                {
                    seat.BookingId = booking.Id;
                    seat.IsActive = true;
                }
                await _context.BookingSeats.AddRangeAsync(seats, ct);

                foreach (var p in passengers)
                {
                    p.BookingId = booking.Id;
                }
                await _context.BookingPassengers.AddRangeAsync(passengers, ct);

                // 5. Update seats to Booked and holds to Confirmed
                foreach (var hold in validHolds)
                {
                    hold.Status = SeatHoldStatus.Confirmed;
                    hold.TripSeat.Status = TripSeatStatus.Booked;
                    hold.TripSeat.UpdatedAt = now;
                }

                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return new BookingResultDto
                {
                    Success = true,
                    Message = "Đặt giữ chỗ thành công.",
                    BookingCode = booking.BookingCode,
                    ExpiresAt = booking.ExpiresAt,
                    TotalAmount = booking.TotalAmount,
                    IsDemoBooking = booking.IsDemoBooking
                };
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync(ct);
                return new BookingResultDto
                {
                    Success = false,
                    Message = "Ghế hoặc phiên giữ chỗ vừa thay đổi. Vui lòng tải lại và thử lại."
                };
            }
            catch (DbUpdateException ex) when (IsDuplicateKey(ex))
            {
                await transaction.RollbackAsync(ct);
                return new BookingResultDto
                {
                    Success = false,
                    Message = "Phiên giữ chỗ đã được sử dụng hoặc ghế vừa được đặt. Vui lòng chọn lại ghế."
                };
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    public async Task<bool> CancelBookingAsync(long bookingId, string ownerIdentifier, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerIdentifier)) return false;
        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                var booking = await _context.Bookings
                    .Include(b => b.BookingSeats)
                        .ThenInclude(bs => bs.TripSeat)
                    .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

                if (booking == null)
                {
                    await transaction.RollbackAsync(ct);
                    return false;
                }

                var ownsBooking = booking.HoldToken != null &&
                    await _context.SeatHolds.AnyAsync(
                        hold => hold.HoldToken == booking.HoldToken &&
                                hold.OwnerIdentifier == ownerIdentifier,
                        ct);
                if (!ownsBooking)
                {
                    await transaction.RollbackAsync(ct);
                    return false;
                }

                if (booking.Status == BookingStatus.Cancelled)
                {
                    await transaction.RollbackAsync(ct);
                    return true;
                }

                if (booking.Status != BookingStatus.PendingPayment ||
                    booking.PaymentStatus != PaymentStatus.Unpaid)
                {
                    await transaction.RollbackAsync(ct);
                    return false;
                }

                var now = DateTimeOffset.UtcNow;
                booking.Status = BookingStatus.Cancelled;
                booking.CancelledAt = now;
                booking.CancellationReason = reason;

                var seatIds = booking.BookingSeats.Select(bs => bs.TripSeatId).ToList();

                foreach (var bs in booking.BookingSeats)
                {
                    bs.IsActive = false;
                    if (bs.TripSeat != null)
                    {
                        bs.TripSeat.Status = TripSeatStatus.Available;
                        bs.TripSeat.UpdatedAt = now;
                    }
                }

                var holds = await _context.SeatHolds
                    .Where(h => seatIds.Contains(h.TripSeatId) &&
                                h.HoldToken == booking.HoldToken &&
                                h.Status == SeatHoldStatus.Confirmed)
                    .ToListAsync(ct);

                foreach (var h in holds)
                {
                    h.Status = SeatHoldStatus.Released;
                }

                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync(ct);
                return false;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    public async Task ExpireStaleBookingsAsync(CancellationToken ct = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                var now = DateTimeOffset.UtcNow;
                var expiredBookings = await _context.Bookings
                    .Include(b => b.BookingSeats)
                        .ThenInclude(bs => bs.TripSeat)
                    .Where(b => b.Status == BookingStatus.PendingPayment &&
                                b.PaymentStatus == PaymentStatus.Unpaid &&
                                b.ExpiresAt <= now)
                    .ToListAsync(ct);

                if (expiredBookings.Count == 0) return;

                foreach (var b in expiredBookings)
                {
                    b.Status = BookingStatus.Expired;
                    var seatIds = b.BookingSeats.Select(bs => bs.TripSeatId).ToList();

                    foreach (var bs in b.BookingSeats)
                    {
                        bs.IsActive = false;
                        if (bs.TripSeat != null)
                        {
                            bs.TripSeat.Status = TripSeatStatus.Available;
                            bs.TripSeat.UpdatedAt = now;
                        }
                    }

                    var holds = await _context.SeatHolds
                        .Where(h => seatIds.Contains(h.TripSeatId) &&
                                    h.HoldToken == b.HoldToken &&
                                    h.Status == SeatHoldStatus.Confirmed)
                        .ToListAsync(ct);

                    foreach (var h in holds)
                    {
                        h.Status = SeatHoldStatus.Expired;
                    }
                }

                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    public async Task<Booking?> GetBookingByCodeAsync(string bookingCode, string ownerIdentifier, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerIdentifier)) return null;
        return await _context.Bookings
            .AsNoTracking()
            .Include(b => b.Trip)
                .ThenInclude(t => t.Route)
                    .ThenInclude(r => r.Operator)
            .Include(b => b.Trip)
                .ThenInclude(t => t.Route)
                    .ThenInclude(r => r.OriginLocation)
            .Include(b => b.Trip)
                .ThenInclude(t => t.Route)
                    .ThenInclude(r => r.DestinationLocation)
            .Include(b => b.Trip)
                .ThenInclude(t => t.Bus)
                    .ThenInclude(bus => bus.BusType)
            .Include(b => b.PickupPoint)
            .Include(b => b.DropoffPoint)
            .Include(b => b.BookingSeats)
            .Include(b => b.Passengers)
            .FirstOrDefaultAsync(b => b.BookingCode == bookingCode &&
                b.HoldToken != null &&
                _context.SeatHolds.Any(h => h.HoldToken == b.HoldToken && h.OwnerIdentifier == ownerIdentifier), ct);
    }

    public async Task<List<Booking>> GetBookingsByPhoneAsync(string phone, string ownerIdentifier, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerIdentifier)) return new List<Booking>();
        return await _context.Bookings
            .AsNoTracking()
            .Include(b => b.Trip)
                .ThenInclude(t => t.Route)
                    .ThenInclude(r => r.Operator)
            .Include(b => b.Trip)
                .ThenInclude(t => t.Route)
                    .ThenInclude(r => r.OriginLocation)
            .Include(b => b.Trip)
                .ThenInclude(t => t.Route)
                    .ThenInclude(r => r.DestinationLocation)
            .Include(b => b.Trip)
                .ThenInclude(t => t.Bus)
                    .ThenInclude(bus => bus.BusType)
            .Include(b => b.PickupPoint)
            .Include(b => b.DropoffPoint)
            .Include(b => b.BookingSeats)
            .Include(b => b.Passengers)
            .Where(b => b.PassengerPhone == phone &&
                        b.HoldToken != null &&
                        _context.SeatHolds.Any(h => h.HoldToken == b.HoldToken &&
                                                   h.OwnerIdentifier == ownerIdentifier))
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task ExpireStaleHoldsAsync(CancellationToken ct = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var now = DateTimeOffset.UtcNow;
            var staleHolds = await _context.SeatHolds
                .Include(hold => hold.TripSeat)
                .Where(hold => hold.Status == SeatHoldStatus.Active && hold.ExpiresAt <= now)
                .ToListAsync(ct);

            foreach (var hold in staleHolds)
            {
                hold.Status = SeatHoldStatus.Expired;
                if (hold.TripSeat != null && hold.TripSeat.Status == TripSeatStatus.Held)
                {
                    hold.TripSeat.Status = TripSeatStatus.Available;
                    hold.TripSeat.UpdatedAt = now;
                }
            }

            if (staleHolds.Count > 0)
                await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });
    }

    private static bool IsDuplicateKey(DbUpdateException exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is SqlException sqlException && sqlException.Number is 2601 or 2627)
                return true;
        }

        return false;
    }
}
