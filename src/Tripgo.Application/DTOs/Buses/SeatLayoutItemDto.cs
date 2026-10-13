namespace Tripgo.Application.DTOs;

public class SeatLayoutItemDto
{
    public long Id { get; set; }
    public string SeatCode { get; set; } = string.Empty;
    public int Floor { get; set; }
    public int Row { get; set; }
    public int Column { get; set; }
    public string SeatType { get; set; } = string.Empty;
    public bool IsAvailable { get; set; } = true;
    public decimal Price { get; set; }

    public SeatLayoutItemDto() { }
    public SeatLayoutItemDto(long id, string seatCode, int floor, int row, int column, string? seatType, bool isAvailable = true)
    {
        Id = id;
        SeatCode = seatCode;
        Floor = floor;
        Row = row;
        Column = column;
        SeatType = seatType ?? string.Empty;
        IsAvailable = isAvailable;
    }
}
