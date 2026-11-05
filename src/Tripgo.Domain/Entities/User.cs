namespace Tripgo.Domain.Entities;

public class User
{
    public long Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public int RoleId { get; set; }
    public Role? Role { get; set; }

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}