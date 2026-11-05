using System;
using System.Collections.Generic;

namespace Tripgo.Application.DTOs;

public class BookingDetailDto
{
    public long Id { get; set; }
    public string BookingCode { get; set; } = string.Empty;
    public long TripId { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string BusTypeName { get; set; } = string.Empty;
    public string BusLicensePlate { get; set; } = string.Empty;
    public DateTimeOffset DepartureTime { get; set; }
    public string PickupPointName { get; set; } = string.Empty;
    public string PickupPointAddress { get; set; } = string.Empty;
    public string DropoffPointName { get; set; } = string.Empty;
    public string DropoffPointAddress { get; set; } = string.Empty;
    public string PassengerName { get; set; } = string.Empty;
    public string PassengerPhone { get; set; } = string.Empty;
    public string? PassengerEmail { get; set; }
    public List<string> SeatCodes { get; set; } = new();
    public decimal BaseAmount { get; set; }
    public decimal SurchargeAmount { get; set; }
    public decimal InsuranceAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? TransactionCode { get; set; }
    public string? PaymentMethod { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool IsDemoBooking { get; set; } = true;
    public List<BookingPassengerDto> Passengers { get; set; } = new();
    public List<PaymentTransactionDto> PaymentTransactions { get; set; } = new();
}
