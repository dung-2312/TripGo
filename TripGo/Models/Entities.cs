using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace TripGo.Models
{
    // 1. DbContext kết nối CSDL
    public class TripGoDbContext : DbContext
    {
        public TripGoDbContext(DbContextOptions<TripGoDbContext> options) : base(options) { }

        public DbSet<Role> Roles { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<BusType> BusTypes { get; set; }
        public DbSet<Bus> Buses { get; set; }
        public DbSet<Route> Routes { get; set; }
        public DbSet<Trip> Trips { get; set; }
        public DbSet<Booking> Bookings { get; set; }
        public DbSet<BookingSeat> BookingSeats { get; set; }
    }

    // 2. Các bảng Entity
    public class Role
    {
        [Key]
        public int RoleId { get; set; }
        [Required, MaxLength(50)]
        public string RoleName { get; set; } = string.Empty; // "Admin", "User"
        public ICollection<User>? Users { get; set; }
    }

    public class User
    {
        [Key]
        public int UserId { get; set; }
        [Required, MaxLength(100)]
        public string FullName { get; set; } = string.Empty;
        [Required, MaxLength(100)]
        public string Email { get; set; } = string.Empty;
        [Required, MaxLength(15)]
        public string PhoneNumber { get; set; } = string.Empty;
        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        public int RoleId { get; set; }
        [ForeignKey("RoleId")]
        public Role? Role { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool IsActive { get; set; } = true;
    }

    public class BusType
    {
        [Key]
        public int BusTypeId { get; set; }
        public string TypeName { get; set; } = string.Empty;
        public int TotalSeats { get; set; }
    }

    public class Bus
    {
        [Key]
        public int BusId { get; set; }
        public string PlateNumber { get; set; } = string.Empty;
        public int BusTypeId { get; set; }
        [ForeignKey("BusTypeId")]
        public BusType? BusType { get; set; }
    }

    public class Route
    {
        [Key]
        public int RouteId { get; set; }
        public string Origin { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public int DistanceKm { get; set; }
    }

    public class Trip
    {
        [Key]
        public int TripId { get; set; }
        public int RouteId { get; set; }
        [ForeignKey("RouteId")]
        public Route? Route { get; set; }
        public int BusId { get; set; }
        [ForeignKey("BusId")]
        public Bus? Bus { get; set; }
        public DateTime DepartureTime { get; set; }
        public DateTime ArrivalTime { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }
        public string Status { get; set; } = "Scheduled";
    }

    public class Booking
    {
        [Key]
        public int BookingId { get; set; }
        public string BookingCode { get; set; } = string.Empty;
        public int? UserId { get; set; }
        [ForeignKey("UserId")]
        public User? User { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; }
        public string BookingStatus { get; set; } = "Confirmed";
        public string PaymentStatus { get; set; } = "Paid";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public ICollection<BookingSeat>? BookingSeats { get; set; }
    }

    public class BookingSeat
    {
        [Key]
        public int BookingSeatId { get; set; }
        public int BookingId { get; set; }
        [ForeignKey("BookingId")]
        public Booking? Booking { get; set; }
        public int TripId { get; set; }
        public string SeatCode { get; set; } = string.Empty;
        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }
    }
}