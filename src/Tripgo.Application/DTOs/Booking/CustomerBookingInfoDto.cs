using System;
using System.Collections.Generic;

namespace Tripgo.Application.DTOs;

public class CustomerBookingInfoDto
{
    public long BookingId { get; set; }
    public long TripId { get; set; }
    public string BookingCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FullName { get => Name; set => Name = value; }
    public string Phone { get; set; } = string.Empty;
    public string PhoneNumber { get => Phone; set => Phone = value; }
    public string? Email { get; set; }
    public bool ContactEmailConfirmed { get; set; }
    public string? PickupPoint { get; set; }
    public long? PickupPointId { get; set; }
    public long? DropoffPointId { get; set; }
    public string SelectedPickupPoint { get; set; } = string.Empty;
    public string SelectedDropoffPoint { get; set; } = string.Empty;
    public string? OwnerIdentifier { get; set; }
    public bool HasInsurance { get; set; } = false;
    public List<string> SeatCodes { get; set; } = new();
    public string RouteName { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string BusTypeName { get; set; } = string.Empty;
    public string BusLicensePlate { get; set; } = string.Empty;
    public DateTimeOffset DepartureTime { get; set; }
    public DateTimeOffset BookedAt { get; set; }
    public decimal BasePrice { get; set; }
    public decimal SurchargeAmount { get; set; }
    public decimal InsuranceAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string BookingStatus { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? TransactionCode { get; set; }
    public string? PaymentMethod { get; set; }
    public List<BookingPassengerDto> Passengers { get; set; } = new();
    public List<PaymentTransactionDto> PaymentTransactions { get; set; } = new();

    public CustomerBookingInfoDto() { }
    public CustomerBookingInfoDto(string name, string phone, string? email = null, string? pickupPoint = null)
    {
        Name = name;
        Phone = phone;
        Email = email;
        PickupPoint = pickupPoint;
    }
}
