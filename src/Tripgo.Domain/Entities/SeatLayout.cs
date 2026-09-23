namespace Tripgo.Domain.Entities;

public class SeatLayout
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int TotalSeats { get; set; }

    public int Floors { get; set; } = 1;

    public bool IsActive { get; set; } = true;

    public ICollection<SeatLayoutItem> Items { get; set; }
        = new List<SeatLayoutItem>();
}