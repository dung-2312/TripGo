using System;
using System.Collections.Generic;
using Tripgo.Domain.Enums;

namespace Tripgo.Domain.Entities;

public class Booking
{
    public long Id { get; set; }

    public string BookingCode { get; set; } = string.Empty;

    public string? HoldToken { get; set; }

    public string? OwnerIdentifier { get; set; }

    public long TripId { get; set; }

    public long? PickupPointId { get; set; }

    public long? DropoffPointId { get; set; }

    public string PickupPointNote { get; set; } = string.Empty;

    public string DropoffPointNote { get; set; } = string.Empty;

    public long? UserId { get; set; }

    public string PassengerName { get; set; } = string.Empty;

    public string PassengerPhone { get; set; } = string.Empty;

    public string? PassengerEmail { get; set; }

    public decimal BaseAmount { get; set; }

    public decimal SurchargeAmount { get; set; }

    public decimal TotalAmount { get; set; }

    public bool HasInsurance { get; set; } = false;

    public decimal InsuranceAmount { get; set; } = 0m;

    public BookingStatus Status { get; set; } = BookingStatus.PendingPayment;

    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ConfirmedAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public string? CancellationReason { get; set; }

    public bool IsDemoBooking { get; set; } = true;

    public Trip Trip { get; set; } = null!;

    public PickupDropoffPoint? PickupPoint { get; set; }

    public PickupDropoffPoint? DropoffPoint { get; set; }

    public User? User { get; set; }

    public ICollection<BookingSeat> BookingSeats { get; set; } = new List<BookingSeat>();

    public ICollection<BookingPassenger> Passengers { get; set; } = new List<BookingPassenger>();

    public ICollection<PaymentTransaction> PaymentTransactions { get; set; } = new List<PaymentTransaction>();
}

