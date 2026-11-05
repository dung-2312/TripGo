using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tripgo.Application.DTOs;

namespace Tripgo.Application.Interfaces;

public interface IPaymentService
{
    Task<PaymentProcessResultDto> ProcessPaymentAsync(ProcessPaymentRequestDto request, CancellationToken ct = default);
    Task<List<PaymentTransactionDto>> GetTransactionsByBookingCodeAsync(string bookingCode, CancellationToken ct = default);
}
