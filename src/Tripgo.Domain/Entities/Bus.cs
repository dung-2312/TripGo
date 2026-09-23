namespace Tripgo.Domain.Entities;

public class Bus
{
    public long Id { get; set; }

    public long OperatorId { get; set; }

    public long BusTypeId { get; set; }

    public long SeatLayoutId { get; set; }

    public string LicensePlate { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? Model { get; set; }

    public int? ManufactureYear { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
        = DateTimeOffset.UtcNow;

    public Operator Operator { get; set; } = null!;

    public BusType BusType { get; set; } = null!;

    public SeatLayout SeatLayout { get; set; } = null!;
}