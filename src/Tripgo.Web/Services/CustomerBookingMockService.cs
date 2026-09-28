using Tripgo.Web.Models;

namespace Tripgo.Web.Services;

public class CustomerBookingMockService : ICustomerBookingService
{
    private static readonly List<CustomerTripDto> _trips = new();
    private static readonly Dictionary<long, List<CustomerSeatDto>> _tripSeats = new();
    private static readonly List<CustomerBookingResultDto> _bookings = new();
    private static readonly object _lock = new();

    public CustomerBookingMockService()
    {
        lock (_lock)
        {
            if (!_trips.Any())
            {
                InitializeMockData();
            }
        }
    }

    private void InitializeMockData()
    {
        var today = DateTime.Today;

        _trips.AddRange(new List<CustomerTripDto>
        {
            new CustomerTripDto
            {
                TripId = 1,
                TripCode = "TG-SG-DL-01",
                OperatorName = "Phương Trang (FUTA)",
                OperatorLogoUrl = "https://cdn-icons-png.flaticon.com/512/3448/3448339.png",
                BusTypeName = "Limousine 34 phòng VIP",
                LicensePlate = "51B-234.56",
                OriginCity = "TP. Hồ Chí Minh",
                DestinationCity = "Đà Lạt",
                PickupPoint = "Bến xe Miền Đông Mới, TP. Thủ Đức",
                DropoffPoint = "Bến xe Liên tỉnh Đà Lạt, Lâm Đồng",
                DepartureTime = new DateTimeOffset(today.AddHours(22), TimeSpan.FromHours(7)),
                ExpectedArrivalTime = new DateTimeOffset(today.AddDays(1).AddHours(5), TimeSpan.FromHours(7)),
                BasePrice = 320000m,
                TotalSeats = 34,
                AvailableSeats = 18,
                Rating = 4.9,
                RatingCount = 340,
                Amenities = new() { "Wifi tốc độ cao", "Cổng sạc Type-C/USB", "Nước suối & khăn lạnh", "Màn hình giải trí", "Chăn gối kháng khuẩn" }
            },
            new CustomerTripDto
            {
                TripId = 2,
                TripCode = "TG-SG-DL-02",
                OperatorName = "Thành Bưởi Limousine",
                OperatorLogoUrl = "https://cdn-icons-png.flaticon.com/512/3448/3448339.png",
                BusTypeName = "Giường nằm cao cấp 40 chỗ",
                LicensePlate = "49B-088.99",
                OriginCity = "TP. Hồ Chí Minh",
                DestinationCity = "Đà Lạt",
                PickupPoint = "Văn phòng Lê Hồng Phong, Quận 5",
                DropoffPoint = "Văn phòng Phan Bội Châu, Đà Lạt",
                DepartureTime = new DateTimeOffset(today.AddHours(23).AddMinutes(30), TimeSpan.FromHours(7)),
                ExpectedArrivalTime = new DateTimeOffset(today.AddDays(1).AddHours(6).AddMinutes(30), TimeSpan.FromHours(7)),
                BasePrice = 280000m,
                TotalSeats = 40,
                AvailableSeats = 25,
                Rating = 4.7,
                RatingCount = 210,
                Amenities = new() { "Wifi miễn phí", "Nước suối", "Điều hòa ion âm" }
            },
            new CustomerTripDto
            {
                TripId = 3,
                TripCode = "TG-SG-NT-01",
                OperatorName = "Phương Trang (FUTA)",
                OperatorLogoUrl = "https://cdn-icons-png.flaticon.com/512/3448/3448339.png",
                BusTypeName = "Limousine 22 Cabin Cung Điện",
                LicensePlate = "79B-012.34",
                OriginCity = "TP. Hồ Chí Minh",
                DestinationCity = "Nha Trang",
                PickupPoint = "Bến xe Miền Đông Cũ, Bình Thạnh",
                DropoffPoint = "Bến xe Phía Nam Nha Trang",
                DepartureTime = new DateTimeOffset(today.AddHours(21).AddMinutes(0), TimeSpan.FromHours(7)),
                ExpectedArrivalTime = new DateTimeOffset(today.AddDays(1).AddHours(5).AddMinutes(30), TimeSpan.FromHours(7)),
                BasePrice = 450000m,
                TotalSeats = 22,
                AvailableSeats = 9,
                Rating = 4.95,
                RatingCount = 185,
                Amenities = new() { "Cabin massage", "Tai nghe bluetooth", "Đèn đọc sách", "Rèm che riêng tư tuyệt đối" }
            },
            new CustomerTripDto
            {
                TripId = 4,
                TripCode = "TG-HN-SP-01",
                OperatorName = "Sao Việt Limousine",
                OperatorLogoUrl = "https://cdn-icons-png.flaticon.com/512/3448/3448339.png",
                BusTypeName = "Cabin Đôi Hoàng Gia 24 phòng",
                LicensePlate = "29B-777.88",
                OriginCity = "Hà Nội",
                DestinationCity = "Sapa",
                PickupPoint = "Văn phòng 789 Giải Phóng, Hà Nội",
                DropoffPoint = "Bến xe Trung tâm Sapa, Lào Cai",
                DepartureTime = new DateTimeOffset(today.AddHours(7).AddMinutes(0), TimeSpan.FromHours(7)),
                ExpectedArrivalTime = new DateTimeOffset(today.AddHours(13).AddMinutes(0), TimeSpan.FromHours(7)),
                BasePrice = 380000m,
                TotalSeats = 24,
                AvailableSeats = 12,
                Rating = 4.85,
                RatingCount = 420,
                Amenities = new() { "Wifi 5G", "Sạc không dây", "Gối massage", "Suất ăn nhẹ & nước uống" }
            },
            new CustomerTripDto
            {
                TripId = 5,
                TripCode = "TG-HN-SP-02",
                OperatorName = "Inter Bus Lines",
                OperatorLogoUrl = "https://cdn-icons-png.flaticon.com/512/3448/3448339.png",
                BusTypeName = "Giường nằm tiêu chuẩn 36 chỗ",
                LicensePlate = "24B-009.68",
                OriginCity = "Hà Nội",
                DestinationCity = "Sapa",
                PickupPoint = "Bến xe Mỹ Đình, Hà Nội",
                DropoffPoint = "Thị xã Sapa",
                DepartureTime = new DateTimeOffset(today.AddHours(22).AddMinutes(30), TimeSpan.FromHours(7)),
                ExpectedArrivalTime = new DateTimeOffset(today.AddDays(1).AddHours(4).AddMinutes(30), TimeSpan.FromHours(7)),
                BasePrice = 290000m,
                TotalSeats = 36,
                AvailableSeats = 16,
                Rating = 4.6,
                RatingCount = 150,
                Amenities = new() { "Wifi", "Khăn lạnh", "Cổng sạc USB" }
            },
            new CustomerTripDto
            {
                TripId = 6,
                TripCode = "TG-DN-HUE-01",
                OperatorName = "Hải Vân Limousine",
                OperatorLogoUrl = "https://cdn-icons-png.flaticon.com/512/3448/3448339.png",
                BusTypeName = "Ghế ngồi VIP 16 chỗ",
                LicensePlate = "43B-099.11",
                OriginCity = "Đà Nẵng",
                DestinationCity = "Huế",
                PickupPoint = "Bến xe Trung tâm Đà Nẵng",
                DropoffPoint = "Bến xe Phía Nam TP. Huế",
                DepartureTime = new DateTimeOffset(today.AddHours(8).AddMinutes(30), TimeSpan.FromHours(7)),
                ExpectedArrivalTime = new DateTimeOffset(today.AddHours(10).AddMinutes(30), TimeSpan.FromHours(7)),
                BasePrice = 160000m,
                TotalSeats = 16,
                AvailableSeats = 10,
                Rating = 4.75,
                RatingCount = 89,
                Amenities = new() { "Nước suối", "Điều hòa mát mẻ", "Ghế ngả 45 độ" }
            },
            new CustomerTripDto
            {
                TripId = 7,
                TripCode = "TG-SG-VT-01",
                OperatorName = "Hoa Mai Vũng Tàu",
                OperatorLogoUrl = "https://cdn-icons-png.flaticon.com/512/3448/3448339.png",
                BusTypeName = "Limousine VIP 9 chỗ đón tận nhà",
                LicensePlate = "72B-015.88",
                OriginCity = "TP. Hồ Chí Minh",
                DestinationCity = "Vũng Tàu",
                PickupPoint = "Sân bay Tân Sơn Nhất / Q1 TP.HCM",
                DropoffPoint = "TP. Vũng Tàu (Đưa đón tận nơi)",
                DepartureTime = new DateTimeOffset(today.AddHours(9).AddMinutes(0), TimeSpan.FromHours(7)),
                ExpectedArrivalTime = new DateTimeOffset(today.AddHours(11).AddMinutes(15), TimeSpan.FromHours(7)),
                BasePrice = 200000m,
                TotalSeats = 9,
                AvailableSeats = 5,
                Rating = 4.9,
                RatingCount = 512,
                Amenities = new() { "Đón trả tận nơi", "Wifi", "Cổng sạc nhanh", "Ghế da cao cấp" }
            }
        });

        // Khởi tạo ghế cho các chuyến
        foreach (var trip in _trips)
        {
            var seats = GenerateSeatsForTrip(trip.TripId, trip.BasePrice);
            _tripSeats[trip.TripId] = seats;
        }

        // Khởi tạo mẫu 1 booking thành công sẵn để tra cứu
        _bookings.Add(new CustomerBookingResultDto
        {
            BookingCode = "TG20260928001",
            BookingTime = DateTimeOffset.Now.AddDays(-1),
            Trip = _trips[0],
            Seats = new() { "A03", "A04" },
            TotalAmount = 640000m,
            PassengerName = "Nguyễn Văn An",
            PassengerPhone = "0987654321",
            PassengerEmail = "nguyenvanan@gmail.com",
            PickupPoint = _trips[0].PickupPoint,
            DropoffPoint = _trips[0].DropoffPoint,
            PaymentStatus = "Đã thanh toán",
            PaymentMethod = "VNPAY-QR",
            PaymentExpiresAt = DateTimeOffset.Now.AddDays(1)
        });
    }

    private List<CustomerSeatDto> GenerateSeatsForTrip(long tripId, decimal basePrice)
    {
        var seats = new List<CustomerSeatDto>();
        // Tầng 1: A01 -> A12
        for (int i = 1; i <= 12; i++)
        {
            var code = $"A{i:D2}";
            bool isBooked = (i % 3 == 0); // Giả lập một số ghế đã bán
            seats.Add(new CustomerSeatDto
            {
                SeatCode = code,
                Floor = 1,
                Row = (i - 1) / 3,
                Column = (i - 1) % 3,
                SeatType = i <= 6 ? "Phòng VIP" : "Standard",
                Price = i <= 6 ? basePrice + 30000 : basePrice,
                Status = isBooked ? SeatStatus.Unavailable : SeatStatus.Available
            });
        }

        // Tầng 2: B01 -> B12
        for (int i = 1; i <= 12; i++)
        {
            var code = $"B{i:D2}";
            bool isBooked = (i % 4 == 0);
            seats.Add(new CustomerSeatDto
            {
                SeatCode = code,
                Floor = 2,
                Row = (i - 1) / 3,
                Column = (i - 1) % 3,
                SeatType = "Standard",
                Price = basePrice,
                Status = isBooked ? SeatStatus.Unavailable : SeatStatus.Available
            });
        }

        return seats;
    }

    public Task<List<string>> GetPopularOriginsAsync()
    {
        var origins = new List<string> { "TP. Hồ Chí Minh", "Hà Nội", "Đà Nẵng", "Nha Trang", "Cần Thơ", "Hải Phòng" };
        return Task.FromResult(origins);
    }

    public Task<List<string>> GetPopularDestinationsAsync()
    {
        var destinations = new List<string> { "Đà Lạt", "Nha Trang", "Sapa", "Vũng Tàu", "Huế", "Quy Nhơn", "Phan Thiết" };
        return Task.FromResult(destinations);
    }

    public Task<List<CustomerTripDto>> SearchTripsAsync(TripSearchFilter filter)
    {
        var query = _trips.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(filter.Origin))
        {
            query = query.Where(t => t.OriginCity.Contains(filter.Origin.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter.Destination))
        {
            query = query.Where(t => t.DestinationCity.Contains(filter.Destination.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (filter.DepartureDate.HasValue)
        {
            query = query.Where(t => t.DepartureTime.Date == filter.DepartureDate.Value.Date);
        }

        // Lọc nhà xe
        if (filter.SelectedOperators != null && filter.SelectedOperators.Any())
        {
            query = query.Where(t => filter.SelectedOperators.Contains(t.OperatorName));
        }

        // Lọc loại xe
        if (filter.SelectedBusTypes != null && filter.SelectedBusTypes.Any())
        {
            query = query.Where(t => filter.SelectedBusTypes.Any(bt => t.BusTypeName.Contains(bt, StringComparison.OrdinalIgnoreCase)));
        }

        // Lọc khung giờ
        if (filter.SelectedTimeFrames != null && filter.SelectedTimeFrames.Any())
        {
            query = query.Where(t =>
            {
                var hour = t.DepartureTime.Hour;
                if (filter.SelectedTimeFrames.Contains("EarlyMorning") && hour >= 0 && hour < 6) return true;
                if (filter.SelectedTimeFrames.Contains("Morning") && hour >= 6 && hour < 12) return true;
                if (filter.SelectedTimeFrames.Contains("Afternoon") && hour >= 12 && hour < 18) return true;
                if (filter.SelectedTimeFrames.Contains("Evening") && hour >= 18 && hour < 24) return true;
                return false;
            });
        }

        // Lọc giá tối đa
        if (filter.MaxPrice.HasValue && filter.MaxPrice.Value > 0)
        {
            query = query.Where(t => t.BasePrice <= filter.MaxPrice.Value);
        }

        // Sắp xếp
        query = filter.SortBy switch
        {
            "PriceAsc" => query.OrderBy(t => t.BasePrice),
            "PriceDesc" => query.OrderByDescending(t => t.BasePrice),
            "RatingDesc" => query.OrderByDescending(t => t.Rating),
            _ => query.OrderBy(t => t.DepartureTime)
        };

        return Task.FromResult(query.ToList());
    }

    public Task<CustomerTripDto?> GetTripDetailsAsync(long tripId)
    {
        var trip = _trips.FirstOrDefault(t => t.TripId == tripId);
        return Task.FromResult(trip);
    }

    public Task<List<CustomerSeatDto>> GetSeatsForTripAsync(long tripId)
    {
        if (_tripSeats.TryGetValue(tripId, out var seats))
        {
            // Trả về clone để tránh lỗi trạng thái đồng thời
            return Task.FromResult(seats.Select(s => new CustomerSeatDto
            {
                SeatCode = s.SeatCode,
                Floor = s.Floor,
                Row = s.Row,
                Column = s.Column,
                SeatType = s.SeatType,
                Price = s.Price,
                Status = s.Status
            }).ToList());
        }

        return Task.FromResult(new List<CustomerSeatDto>());
    }

    public Task<(bool Success, string Message, CustomerBookingResultDto? Result)> CreateBookingAsync(CustomerBookingRequest request)
    {
        lock (_lock)
        {
            var trip = _trips.FirstOrDefault(t => t.TripId == request.TripId);
            if (trip == null)
            {
                return Task.FromResult((false, "Không tìm thấy chuyến xe!", (CustomerBookingResultDto?)null));
            }

            if (!_tripSeats.TryGetValue(request.TripId, out var seats))
            {
                return Task.FromResult((false, "Không tìm thấy sơ đồ ghế!", (CustomerBookingResultDto?)null));
            }

            // Kiểm tra ghế có bị ai mua chưa
            var conflictingSeats = seats.Where(s => request.SelectedSeats.Contains(s.SeatCode) && s.Status == SeatStatus.Unavailable).ToList();
            if (conflictingSeats.Any())
            {
                var codes = string.Join(", ", conflictingSeats.Select(s => s.SeatCode));
                return Task.FromResult((false, $"Ghế [{codes}] vừa có người khác đặt. Vui lòng chọn ghế khác!", (CustomerBookingResultDto?)null));
            }

            // Đánh dấu ghế đã bán
            decimal totalAmount = 0;
            foreach (var seat in seats.Where(s => request.SelectedSeats.Contains(s.SeatCode)))
            {
                seat.Status = SeatStatus.Unavailable;
                totalAmount += seat.Price;
            }

            trip.AvailableSeats = Math.Max(0, trip.AvailableSeats - request.SelectedSeats.Count);

            string bookingCode = "TG" + DateTime.Now.ToString("yyyyMMddHHmmss");
            var result = new CustomerBookingResultDto
            {
                BookingCode = bookingCode,
                BookingTime = DateTimeOffset.Now,
                Trip = trip,
                Seats = request.SelectedSeats,
                TotalAmount = totalAmount,
                PassengerName = request.FullName,
                PassengerPhone = request.PhoneNumber,
                PassengerEmail = request.Email,
                PickupPoint = request.PickupPoint,
                DropoffPoint = request.DropoffPoint,
                PaymentStatus = "Chờ thanh toán",
                PaymentMethod = request.PaymentMethod,
                PaymentExpiresAt = DateTimeOffset.Now.AddMinutes(15)
            };

            _bookings.Add(result);
            return Task.FromResult<(bool, string, CustomerBookingResultDto?)>((true, "Đặt vé thành công!", result));
        }
    }

    public Task<CustomerBookingResultDto?> GetBookingResultAsync(string bookingCode)
    {
        var booking = _bookings.FirstOrDefault(b => b.BookingCode.Equals(bookingCode, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(booking);
    }

    public Task<List<CustomerBookingResultDto>> SearchBookingsAsync(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return Task.FromResult(new List<CustomerBookingResultDto>());

        keyword = keyword.Trim();
        var list = _bookings
            .Where(b => b.BookingCode.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        b.PassengerPhone.Contains(keyword))
            .OrderByDescending(b => b.BookingTime)
            .ToList();

        return Task.FromResult(list);
    }
}
