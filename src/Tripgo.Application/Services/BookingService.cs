using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Tripgo.Application.DTOs;
using Tripgo.Application.Interfaces;
using Tripgo.Application.Interfaces.Persistence;
using Tripgo.Domain;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;

namespace Tripgo.Application.Services;

public class BookingService : IBookingService
{
    private readonly IBookingRepository _bookingRepository;
    private readonly IBookingSessionContext _bookingSessionContext;

    public BookingService(IBookingRepository bookingRepository, IBookingSessionContext bookingSessionContext)
    {
        _bookingRepository = bookingRepository ?? throw new ArgumentNullException(nameof(bookingRepository));
        _bookingSessionContext = bookingSessionContext ?? throw new ArgumentNullException(nameof(bookingSessionContext));
    }

    public async Task EnsureDemoTripInventoryAsync(long tripId, CancellationToken ct = default)
    {
        var trip = await _bookingRepository.GetTripForBookingAsync(tripId, ct);
        if (trip == null || !BookingRules.IsSandboxTrip(trip))
        {
            return;
        }

        var existingSeats = await _bookingRepository.GetTripSeatsAsync(tripId, ct);
        if (existingSeats.Count > 0)
        {
            return;
        }

        var layoutItems = trip.Bus?.SeatLayout?.Items?.Where(i => i.IsActive).ToList() ?? new List<SeatLayoutItem>();
        if (layoutItems.Count == 0)
        {
            return;
        }

        var newSeats = new List<TripSeat>();
        foreach (var item in layoutItems)
        {
            var isCouple = string.Equals(item.SeatType, "DoubleBed", StringComparison.OrdinalIgnoreCase);
            var seatPrice = trip.BasePrice + (isCouple ? 100_000m : 0m);

            newSeats.Add(new TripSeat
            {
                TripId = trip.Id,
                SeatLayoutItemId = item.Id,
                SeatCode = item.SeatCode,
                SeatType = item.SeatType,
                Floor = item.Floor,
                Row = item.Row,
                Column = item.Column,
                Price = seatPrice,
                Status = TripSeatStatus.Available,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await _bookingRepository.AddTripSeatsAsync(newSeats, ct);
    }

    public async Task<List<TripSeatDto>> GetSeatsForTripAsync(long tripId, CancellationToken ct = default)
    {
        var trip = await _bookingRepository.GetTripForBookingAsync(tripId, ct);
        if (trip == null)
        {
            return new List<TripSeatDto>();
        }

        if (!BookingRules.IsSandboxTrip(trip))
        {
            // Reference mode: No online booking, no persistent seat inventory
            return new List<TripSeatDto>();
        }

        await EnsureDemoTripInventoryAsync(tripId, ct);

        var seats = await _bookingRepository.GetTripSeatsAsync(tripId, ct);
        return seats.Select(s => new TripSeatDto
        {
            Id = s.Id,
            TripId = s.TripId,
            SeatCode = s.SeatCode,
            SeatType = s.SeatType,
            Floor = s.Floor,
            Row = s.Row,
            Column = s.Column,
            Price = s.Price,
            Status = s.Status.ToString(),
            IsAvailable = s.Status == TripSeatStatus.Available
        }).ToList();
    }

    public async Task<SeatHoldResultDto> HoldSeatsAsync(SeatHoldRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ownerIdentifier = await _bookingSessionContext.GetOwnerIdentifierAsync();
        if (string.IsNullOrWhiteSpace(ownerIdentifier))
        {
            return new SeatHoldResultDto
            {
                Success = false,
                Message = "Phiên đặt chỗ không hợp lệ. Vui lòng tải lại trang và thử lại."
            };
        }

        if (request.SeatCodes == null || request.SeatCodes.Count == 0 ||
            request.SeatCodes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.SeatCodes.Count)
        {
            return new SeatHoldResultDto
            {
                Success = false,
                Message = "Vui lòng chọn danh sách ghế không rỗng và không chứa ghế trùng lặp."
            };
        }

        var trip = await _bookingRepository.GetTripForBookingAsync(request.TripId, ct);
        if (trip == null)
        {
            return new SeatHoldResultDto
            {
                Success = false,
                Message = "Không tìm thấy thông tin chuyến xe."
            };
        }

        if (!BookingRules.IsSandboxTrip(trip))
        {
            return new SeatHoldResultDto
            {
                Success = false,
                Message = "Chuyến xe này thuộc Lịch trình tham khảo của nhà xe. Hệ thống không mở bán hoặc giữ chỗ trực tuyến."
            };
        }

        var saleBlockReason = BookingRules.GetSaleBlockReason(trip, DateTimeOffset.UtcNow);
        if (saleBlockReason != null)
        {
            return new SeatHoldResultDto { Success = false, Message = saleBlockReason };
        }

        if (request.SeatCodes.Count > BookingRules.MaxSeatsPerBooking)
        {
            return new SeatHoldResultDto
            {
                Success = false,
                Message = $"Mỗi đơn chỉ được đặt tối đa {BookingRules.MaxSeatsPerBooking} ghế."
            };
        }

        await EnsureDemoTripInventoryAsync(request.TripId, ct);

        var holdToken = string.IsNullOrWhiteSpace(request.ExistingHoldToken)
            ? $"HOLD-{Guid.NewGuid():N}"
            : request.ExistingHoldToken.Trim();
        if (holdToken.Length > 100 || !holdToken.StartsWith("HOLD-", StringComparison.OrdinalIgnoreCase))
        {
            return new SeatHoldResultDto
            {
                Success = false,
                Message = "Mã giữ chỗ không hợp lệ."
            };
        }

        var holdMinutes = Math.Clamp(request.HoldMinutes, 1, 30);
        return await _bookingRepository.HoldSeatsAsync(request.TripId, request.SeatCodes, holdToken, holdMinutes, ownerIdentifier, ct);
    }

    public async Task<bool> ReleaseHoldAsync(string holdToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(holdToken)) return false;
        var ownerIdentifier = await _bookingSessionContext.GetOwnerIdentifierAsync();
        if (string.IsNullOrWhiteSpace(ownerIdentifier)) return false;
        return await _bookingRepository.ReleaseHoldAsync(holdToken.Trim(), ownerIdentifier, ct);
    }

    public async Task<BookingResultDto> CreateBookingAsync(CreateBookingRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ownerIdentifier = await _bookingSessionContext.GetOwnerIdentifierAsync();
        if (string.IsNullOrWhiteSpace(ownerIdentifier))
        {
            return new BookingResultDto
            {
                Success = false,
                Message = "Phiên đặt chỗ không hợp lệ. Vui lòng tải lại trang và thử lại."
            };
        }

        // 1. Validate contact info
        var passengerName = request.PassengerName?.Trim() ?? string.Empty;
        if (passengerName.Length < 2)
        {
            return new BookingResultDto
            {
                Success = false,
                Message = "Họ tên hành khách phải có ít nhất 2 ký tự."
            };
        }

        var normalizedPhone = NormalizePhone(request.PassengerPhone);
        if (normalizedPhone.Length < 9 || normalizedPhone.Length > 11 || !normalizedPhone.StartsWith("0"))
        {
            return new BookingResultDto
            {
                Success = false,
                Message = "Số điện thoại liên hệ không hợp lệ. Vui lòng nhập số điện thoại Việt Nam (10 chữ số bắt đầu bằng 0)."
            };
        }

        var email = request.PassengerEmail?.Trim();
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 ||
            !new EmailAddressAttribute().IsValid(email))
        {
            return new BookingResultDto
            {
                Success = false,
                Message = "Vui lòng nhập địa chỉ email hợp lệ để nhận vé điện tử."
            };
        }

        if (!request.ContactEmailConfirmed)
        {
            return new BookingResultDto
            {
                Success = false,
                Message = "Vui lòng xác nhận địa chỉ email nhận vé điện tử là chính xác."
            };
        }

        // 2. Validate HoldToken
        if (string.IsNullOrWhiteSpace(request.HoldToken) ||
            request.HoldToken.Length > 100 ||
            !request.HoldToken.StartsWith("HOLD-", StringComparison.OrdinalIgnoreCase))
        {
            return new BookingResultDto
            {
                Success = false,
                Message = "Phiên giữ chỗ không hợp lệ hoặc đã hết hạn. Vui lòng chọn lại ghế."
            };
        }

        if (request.SeatCodes == null || request.SeatCodes.Count == 0 ||
            request.SeatCodes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.SeatCodes.Count)
        {
            return new BookingResultDto
            {
                Success = false,
                Message = "Danh sách ghế đặt không được để trống hoặc chứa ghế trùng lặp."
            };
        }

        var trip = await _bookingRepository.GetTripForBookingAsync(request.TripId, ct);
        if (trip == null)
        {
            return new BookingResultDto
            {
                Success = false,
                Message = "Không tìm thấy thông tin chuyến xe."
            };
        }

        if (!BookingRules.IsSandboxTrip(trip))
        {
            return new BookingResultDto
            {
                Success = false,
                Message = "Chuyến xe này thuộc Lịch trình tham khảo của nhà xe. Đặt vé trực tuyến bị từ chối."
            };
        }

        var saleBlockReason = BookingRules.GetSaleBlockReason(trip, DateTimeOffset.UtcNow);
        if (saleBlockReason != null)
        {
            return new BookingResultDto { Success = false, Message = saleBlockReason };
        }

        // 3. Validate pickup and dropoff points against trip stops or route stops
        var routeStops = trip.Route?.Stops?.OrderBy(s => s.StopOrder).ToList() ?? new List<RouteStop>();
        var tripStops = trip.Stops?.OrderBy(s => s.StopOrder).ToList() ?? new List<TripStop>();

        string pickupName;
        int? pickupOrder = null;
        if (request.PickupPointId.HasValue)
        {
            var pId = request.PickupPointId.Value;
            var tripStop = tripStops.FirstOrDefault(s => s.PointId == pId);
            var routeStop = routeStops.FirstOrDefault(s => s.PointId == pId);

            if (tripStop == null && routeStop == null)
            {
                return new BookingResultDto
                {
                    Success = false,
                    Message = "Điểm đón được chọn không thuộc lộ trình xác thực của chuyến xe này."
                };
            }

            var allowPickup = tripStop?.AllowPickup ?? routeStop?.AllowPickup ?? false;
            if (!allowPickup)
            {
                return new BookingResultDto
                {
                    Success = false,
                    Message = "Điểm dừng này không cho phép đón khách."
                };
            }

            pickupOrder = tripStop?.StopOrder ?? routeStop?.StopOrder;
            pickupName = tripStop?.Point?.Name ?? routeStop?.Point?.Name ?? "Điểm đón";
        }
        else
        {
            pickupName = trip.Route?.OriginLocation?.Name ?? "Điểm đi";
        }

        string dropoffName;
        int? dropoffOrder = null;
        if (request.DropoffPointId.HasValue)
        {
            var dId = request.DropoffPointId.Value;
            var tripStop = tripStops.FirstOrDefault(s => s.PointId == dId);
            var routeStop = routeStops.FirstOrDefault(s => s.PointId == dId);

            if (tripStop == null && routeStop == null)
            {
                return new BookingResultDto
                {
                    Success = false,
                    Message = "Điểm trả được chọn không thuộc lộ trình xác thực của chuyến xe này."
                };
            }

            var allowDropoff = tripStop?.AllowDropoff ?? routeStop?.AllowDropoff ?? false;
            if (!allowDropoff)
            {
                return new BookingResultDto
                {
                    Success = false,
                    Message = "Điểm dừng này không cho phép trả khách."
                };
            }

            dropoffOrder = tripStop?.StopOrder ?? routeStop?.StopOrder;
            dropoffName = tripStop?.Point?.Name ?? routeStop?.Point?.Name ?? "Điểm trả";
        }
        else
        {
            dropoffName = trip.Route?.DestinationLocation?.Name ?? "Điểm đến";
        }

        // Validate stop sequence / direction: pickup stop must precede dropoff stop
        if (pickupOrder.HasValue && dropoffOrder.HasValue && pickupOrder.Value >= dropoffOrder.Value)
        {
            return new BookingResultDto
            {
                Success = false,
                Message = "Lộ trình không hợp lệ: Điểm đón phải nằm trước điểm trả theo hướng di chuyển."
            };
        }

        // 4. Validate requested seats
        var tripSeats = await _bookingRepository.GetTripSeatsAsync(request.TripId, ct);
        var requestedSeats = tripSeats.Where(s => request.SeatCodes.Contains(s.SeatCode, StringComparer.OrdinalIgnoreCase)).ToList();

        if (requestedSeats.Count != request.SeatCodes.Count)
        {
            return new BookingResultDto
            {
                Success = false,
                Message = "Một số ghế được yêu cầu không tồn tại trên chuyến xe này."
            };
        }

        // 5. Server-authoritative pricing (Insurance disabled temporarily to avoid charging unsupported fees)
        var totalSeatsPrice = requestedSeats.Sum(s => s.Price);
        var baseAmount = requestedSeats.Count * trip.BasePrice;
        var surchargeAmount = requestedSeats.Sum(s => Math.Max(0m, s.Price - trip.BasePrice));
        if (baseAmount > totalSeatsPrice)
        {
            baseAmount = totalSeatsPrice;
            surchargeAmount = 0m;
        }

        var insuranceAmount = 0m;
        var hasInsurance = false;
        var totalAmount = baseAmount + surchargeAmount + insuranceAmount;

        var bookingCode = GenerateBookingCode();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(BookingRules.PaymentWindowMinutes);

        var userId = await _bookingSessionContext.GetUserIdAsync();

        var booking = new Booking
        {
            BookingCode = bookingCode,
            TripId = trip.Id,
            PickupPointId = request.PickupPointId,
            DropoffPointId = request.DropoffPointId,
            PickupPointNote = pickupName,
            DropoffPointNote = dropoffName,
            PassengerName = passengerName,
            PassengerPhone = normalizedPhone,
            PassengerEmail = email,
            BaseAmount = baseAmount,
            SurchargeAmount = surchargeAmount,
            InsuranceAmount = insuranceAmount,
            HasInsurance = hasInsurance,
            TotalAmount = totalAmount,
            Status = BookingStatus.PendingPayment,
            PaymentStatus = PaymentStatus.Unpaid,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
            IsDemoBooking = true,
            OwnerIdentifier = ownerIdentifier,
            UserId = userId
        };

        var bookingSeats = requestedSeats.Select(s => new BookingSeat
        {
            TripSeatId = s.Id,
            SeatCode = s.SeatCode,
            Price = s.Price,
            IsActive = true
        }).ToList();

        var passengers = new List<BookingPassenger>();
        if (request.Passengers != null && request.Passengers.Count > 0)
        {
            if (request.Passengers.Count != request.SeatCodes.Count)
            {
                return new BookingResultDto
                {
                    Success = false,
                    Message = $"Số lượng thông tin hành khách ({request.Passengers.Count}) phải khớp với số lượng ghế đặt ({request.SeatCodes.Count})."
                };
            }

            var assignedSeats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in request.Passengers)
            {
                if (!string.IsNullOrWhiteSpace(p.SeatCode))
                {
                    var code = p.SeatCode.Trim();
                    if (!request.SeatCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
                    {
                        return new BookingResultDto
                        {
                            Success = false,
                            Message = $"Ghế {code} của hành khách không nằm trong danh sách ghế đặt."
                        };
                    }

                    if (!assignedSeats.Add(code))
                    {
                        return new BookingResultDto
                        {
                            Success = false,
                            Message = $"Ghế {code} bị gán trùng lặp cho nhiều hành khách."
                        };
                    }
                }

                var pName = p.FullName?.Trim() ?? string.Empty;
                if (pName.Length < 2)
                {
                    return new BookingResultDto
                    {
                        Success = false,
                        Message = "Họ tên của mỗi hành khách phải có ít nhất 2 ký tự."
                    };
                }

                var pPhone = NormalizePhone(p.PhoneNumber);
                if (pPhone.Length < 9 || pPhone.Length > 11 || !pPhone.StartsWith("0"))
                {
                    return new BookingResultDto
                    {
                        Success = false,
                        Message = $"Số điện thoại của hành khách {pName} không hợp lệ (10 chữ số bắt đầu bằng 0)."
                    };
                }

                var pEmail = p.Email?.Trim();
                if (!string.IsNullOrWhiteSpace(pEmail) &&
                    (pEmail.Length > 254 || !new EmailAddressAttribute().IsValid(pEmail)))
                {
                    return new BookingResultDto
                    {
                        Success = false,
                        Message = $"Định dạng email của hành khách {pName} không hợp lệ."
                    };
                }

                passengers.Add(new BookingPassenger
                {
                    FullName = pName,
                    PhoneNumber = pPhone,
                    Email = pEmail,
                    IdentityCard = p.IdentityCard?.Trim(),
                    IsPrimaryContact = p.IsPrimaryContact
                });
            }

            var primaryCount = passengers.Count(p => p.IsPrimaryContact);
            if (primaryCount == 0)
            {
                passengers[0].IsPrimaryContact = true;
            }
            else if (primaryCount > 1)
            {
                var firstFound = false;
                foreach (var p in passengers)
                {
                    if (p.IsPrimaryContact)
                    {
                        if (!firstFound) firstFound = true;
                        else p.IsPrimaryContact = false;
                    }
                }
            }
        }
        else
        {
            for (int i = 0; i < requestedSeats.Count; i++)
            {
                passengers.Add(new BookingPassenger
                {
                    FullName = passengerName,
                    PhoneNumber = normalizedPhone,
                    Email = email,
                    IsPrimaryContact = i == 0
                });
            }
        }

        return await _bookingRepository.CreateBookingAsync(booking, bookingSeats, passengers, request.HoldToken, ownerIdentifier, ct);
    }

    public async Task<bool> CancelBookingAsync(long bookingId, string reason, CancellationToken ct = default)
    {
        var ownerIdentifier = await _bookingSessionContext.GetOwnerIdentifierAsync();
        var userId = await _bookingSessionContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(ownerIdentifier) && !userId.HasValue) return false;
        return await _bookingRepository.CancelBookingAsync(bookingId, ownerIdentifier, userId, reason, ct);
    }

    public async Task<BookingDetailDto?> GetBookingByCodeAsync(string bookingCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bookingCode)) return null;
        var ownerIdentifier = await _bookingSessionContext.GetOwnerIdentifierAsync();
        var userId = await _bookingSessionContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(ownerIdentifier) && !userId.HasValue) return null;

        var booking = await _bookingRepository.GetBookingByCodeAsync(bookingCode.Trim(), ownerIdentifier, userId, ct);
        return booking == null ? null : MapToDetail(booking);
    }

    public async Task<List<BookingDetailDto>> GetBookingsByPhoneAsync(string phone, CancellationToken ct = default)
    {
        var normalizedPhone = NormalizePhone(phone);
        if (string.IsNullOrEmpty(normalizedPhone)) return new List<BookingDetailDto>();
        var ownerIdentifier = await _bookingSessionContext.GetOwnerIdentifierAsync();
        var userId = await _bookingSessionContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(ownerIdentifier) && !userId.HasValue) return new List<BookingDetailDto>();

        var bookings = await _bookingRepository.GetBookingsByPhoneAsync(normalizedPhone, ownerIdentifier, userId, ct);
        return bookings.Select(MapToDetail).ToList();
    }

    public async Task<List<BookingDetailDto>> GetBookingsForCurrentUserAsync(CancellationToken ct = default)
    {
        var userId = await _bookingSessionContext.GetUserIdAsync();
        if (userId.HasValue && userId.Value > 0)
        {
            var userBookings = await _bookingRepository.GetBookingsByUserIdAsync(userId.Value, ct);
            return userBookings.Select(MapToDetail).ToList();
        }

        var ownerIdentifier = await _bookingSessionContext.GetOwnerIdentifierAsync();
        if (!string.IsNullOrWhiteSpace(ownerIdentifier))
        {
            var guestBookings = await _bookingRepository.GetBookingsByOwnerIdentifierAsync(ownerIdentifier, ct);
            return guestBookings.Select(MapToDetail).ToList();
        }

        return new List<BookingDetailDto>();
    }

    private static BookingDetailDto MapToDetail(Booking b)
    {
        return new BookingDetailDto
        {
            Id = b.Id,
            BookingCode = b.BookingCode,
            TripId = b.TripId,
            RouteName = b.Trip?.Route?.Name ?? string.Empty,
            OperatorName = b.Trip?.Route?.Operator?.Name ?? string.Empty,
            BusTypeName = b.Trip?.Bus?.BusType?.Name ?? string.Empty,
            BusLicensePlate = b.Trip?.Bus?.LicensePlate ?? string.Empty,
            DepartureTime = b.Trip?.DepartureTime ?? DateTimeOffset.MinValue,
            PickupPointName = b.PickupPointNote,
            PickupPointAddress = b.PickupPoint?.Address ?? string.Empty,
            DropoffPointName = b.DropoffPointNote,
            DropoffPointAddress = b.DropoffPoint?.Address ?? string.Empty,
            PassengerName = b.PassengerName,
            PassengerPhone = b.PassengerPhone,
            PassengerEmail = b.PassengerEmail,
            SeatCodes = b.BookingSeats?.Select(s => s.SeatCode).OrderBy(s => s).ToList() ?? new List<string>(),
            BaseAmount = b.BaseAmount,
            SurchargeAmount = b.SurchargeAmount,
            InsuranceAmount = b.InsuranceAmount,
            TotalAmount = b.TotalAmount,
            Status = b.Status.ToString(),
            PaymentStatus = b.PaymentStatus.ToString(),
            ConfirmedAt = b.ConfirmedAt,
            TransactionCode = b.PaymentTransactions?.FirstOrDefault(t => t.Status == PaymentStatus.Paid)?.TransactionCode
                ?? b.PaymentTransactions?.OrderByDescending(t => t.CreatedAt).FirstOrDefault()?.TransactionCode,
            PaymentMethod = b.PaymentTransactions?.FirstOrDefault(t => t.Status == PaymentStatus.Paid)?.PaymentMethod
                ?? b.PaymentTransactions?.OrderByDescending(t => t.CreatedAt).FirstOrDefault()?.PaymentMethod,
            CreatedAt = b.CreatedAt,
            ExpiresAt = b.ExpiresAt,
            IsDemoBooking = b.IsDemoBooking,
            Passengers = b.Passengers?.Select(p => new BookingPassengerDto
            {
                FullName = p.FullName,
                PhoneNumber = p.PhoneNumber,
                Email = p.Email,
                IdentityCard = p.IdentityCard,
                IsPrimaryContact = p.IsPrimaryContact
            }).ToList() ?? new List<BookingPassengerDto>(),
            PaymentTransactions = b.PaymentTransactions?.Select(t => new PaymentTransactionDto
            {
                Id = t.Id,
                BookingId = t.BookingId,
                TransactionCode = t.TransactionCode,
                PaymentMethod = t.PaymentMethod,
                Amount = t.Amount,
                Status = t.Status.ToString(),
                ProviderReference = t.ProviderReference,
                Note = t.Note,
                IsSandbox = t.IsSandbox,
                CreatedAt = t.CreatedAt,
                CompletedAt = t.CompletedAt
            }).ToList() ?? new List<PaymentTransactionDto>()
        };
    }

    private static string GenerateBookingCode()
    {
        var hex = Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
        return $"TRIPGO-{hex}";
    }

    private static string NormalizePhone(string? phone) =>
        new((phone ?? string.Empty).Where(char.IsDigit).ToArray());
}
