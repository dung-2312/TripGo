using System.ComponentModel.DataAnnotations;

namespace Tripgo.Web.Models;

public enum SeatStatus
{
    Available,    // Ghế trống, có thể chọn
    Unavailable,  // Đã có người mua, bị khóa.
    Selected      // Đang được người dùng chọn.
}

public class CustomerTripDto
{
    public long TripId { get; set; }
    public string TripCode { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string? OperatorLogoUrl { get; set; }
    public string BusTypeName { get; set; } = string.Empty;
    public string LicensePlate { get; set; } = string.Empty;
    public string OriginCity { get; set; } = string.Empty;
    public string DestinationCity { get; set; } = string.Empty;
    public string PickupPoint { get; set; } = string.Empty;
    public string DropoffPoint { get; set; } = string.Empty;
    public DateTimeOffset DepartureTime { get; set; }
    public DateTimeOffset ExpectedArrivalTime { get; set; }
    public decimal BasePrice { get; set; }
    public int TotalSeats { get; set; }
    public int AvailableSeats { get; set; }
    public double Rating { get; set; } = 4.8;
    public int RatingCount { get; set; } = 120;
    public List<string> Amenities { get; set; } = new();

    public TimeSpan Duration => ExpectedArrivalTime - DepartureTime;
}

public class CustomerSeatDto
{
    public string SeatCode { get; set; } = string.Empty;
    public int Floor { get; set; } = 1; // 1: Tầng dưới, 2: Tầng trên
    public int Row { get; set; }
    public int Column { get; set; }
    public string SeatType { get; set; } = "Standard"; // Standard, VIP, Phòng VIP
    public decimal Price { get; set; }
    public SeatStatus Status { get; set; } = SeatStatus.Available;
}

public class CustomerBookingRequest
{
    public long TripId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn ít nhất một ghế")]
    public List<string> SelectedSeats { get; set; } = new();

    [Required(ErrorMessage = "Vui lòng nhập họ và tên hành khách")]
    [MinLength(2, ErrorMessage = "Họ tên quá ngắn")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại liên hệ")]
    [Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập email")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng chọn điểm đón")]
    public string PickupPoint { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng chọn điểm trả")]
    public string DropoffPoint { get; set; } = string.Empty;

    public string? Note { get; set; }

    public string PaymentMethod { get; set; } = "VNPayQR";
}

public class CustomerBookingResultDto
{
    public string BookingCode { get; set; } = string.Empty;
    public DateTimeOffset BookingTime { get; set; }
    public CustomerTripDto Trip { get; set; } = new();
    public List<string> Seats { get; set; } = new();
    public decimal TotalAmount { get; set; }
    public string PassengerName { get; set; } = string.Empty;
    public string PassengerPhone { get; set; } = string.Empty;
    public string PassengerEmail { get; set; } = string.Empty;
    public string PickupPoint { get; set; } = string.Empty;
    public string DropoffPoint { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = "Chờ thanh toán";
    public string PaymentMethod { get; set; } = "Chuyển khoản QR";
    public DateTimeOffset PaymentExpiresAt { get; set; }
}

public class TripSearchFilter
{
    public string? Origin { get; set; }
    public string? Destination { get; set; }
    public DateTime? DepartureDate { get; set; }

    // Bộ lọc nâng cao
    public List<string> SelectedOperators { get; set; } = new();
    public List<string> SelectedBusTypes { get; set; } = new();
    public List<string> SelectedTimeFrames { get; set; } = new(); // "EarlyMorning", "Morning", "Afternoon", "Evening"
    public decimal? MaxPrice { get; set; }
    public string SortBy { get; set; } = "TimeAsc"; // "TimeAsc", "PriceAsc", "PriceDesc", "RatingDesc"
}
