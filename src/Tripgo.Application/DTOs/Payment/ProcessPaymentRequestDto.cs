namespace Tripgo.Application.DTOs;

public class ProcessPaymentRequestDto
{
    public string BookingCode { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = "SANDBOX_ATM";
    public decimal Amount { get; set; }
    public bool SimulateSuccess { get; set; } = true;
    public string? FailureReason { get; set; }
}
