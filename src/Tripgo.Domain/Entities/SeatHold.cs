using System;
using Tripgo.Domain.Enums;

namespace Tripgo.Domain.Entities;

public class SeatHold
{
    public long Id { get; set; }

    public long TripSeatId { get; set; }

    public string HoldToken { get; set; } = string.Empty;

    public string? OwnerIdentifier { get; set; }

    public DateTimeOffset HeldAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ExpiresAt { get; set; }

    public SeatHoldStatus Status { get; set; } = SeatHoldStatus.Active;

    public TripSeat TripSeat { get; set; } = null!;
}
