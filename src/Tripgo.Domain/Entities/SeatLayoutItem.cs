namespace Tripgo.Domain.Entities;

public class SeatLayoutItem
{
    public long Id { get; set; }

    public long SeatLayoutId { get; set; }

    public string SeatCode { get; set; } = string.Empty;

    public int Floor { get; set; } = 1;

    public int Row { get; set; }

    public int Column { get; set; }

    public string SeatType { get; set; } = "Standard";

    public bool IsActive { get; set; } = true;

    public SeatLayout SeatLayout { get; set; } = null!;
}