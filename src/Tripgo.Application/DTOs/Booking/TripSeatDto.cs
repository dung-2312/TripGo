namespace Tripgo.Application.DTOs;

public class TripSeatDto
{
    public long Id { get; set; }
    public long TripId { get; set; }
    public string SeatCode { get; set; } = string.Empty;
    public string SeatType { get; set; } = string.Empty;
    public int Floor { get; set; }
    public int Row { get; set; }
    public int Column { get; set; }
    public decimal Price { get; set; }
    public string Status { get; set; } = "Available";
    public bool IsAvailable { get; set; }
}
