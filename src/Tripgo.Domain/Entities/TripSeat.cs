using System;
using System.Collections.Generic;
using Tripgo.Domain.Enums;

namespace Tripgo.Domain.Entities;

public class TripSeat
{
    public long Id { get; set; }

    public long TripId { get; set; }

    public long? SeatLayoutItemId { get; set; }

    public string SeatCode { get; set; } = string.Empty;

    public string SeatType { get; set; } = "Bed";

    public int Floor { get; set; } = 1;

    public int Row { get; set; } = 1;

    public int Column { get; set; } = 1;

    public decimal Price { get; set; }

    public TripSeatStatus Status { get; set; } = TripSeatStatus.Available;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public Trip Trip { get; set; } = null!;

    public SeatLayoutItem? SeatLayoutItem { get; set; }

    public ICollection<SeatHold> SeatHolds { get; set; } = new List<SeatHold>();

    public ICollection<BookingSeat> BookingSeats { get; set; } = new List<BookingSeat>();
}
