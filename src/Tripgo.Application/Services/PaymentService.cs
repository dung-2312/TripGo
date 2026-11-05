using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tripgo.Application.DTOs;
using Tripgo.Application.Interfaces;
using Tripgo.Application.Interfaces.Persistence;
using Tripgo.Domain.Enums;

namespace Tripgo.Application.Services;

public class PaymentService : IPaymentService
{
    private readonly IBookingRepository _bookingRepository;
    private readonly IBookingSessionContext _bookingSessionContext;

    public PaymentService(IBookingRepository bookingRepository, IBookingSessionContext bookingSessionContext)
    {
        _bookingRepository = bookingRepository ?? throw new ArgumentNullException(nameof(bookingRepository));
        _bookingSessionContext = bookingSessionContext ?? throw new ArgumentNullException(nameof(bookingSessionContext));
    }

    public async Task<PaymentProcessResultDto> ProcessPaymentAsync(ProcessPaymentRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ownerIdentifier = await _bookingSessionContext.GetOwnerIdentifierAsync();
        var userId = await _bookingSessionContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(ownerIdentifier) && !userId.HasValue)
        {
            return new PaymentProcessResultDto
            {
                Success = false,
                Message = "Phiên giao dịch không hợp lệ. Vui lòng tải lại trang và thử lại."
            };
        }

        if (string.IsNullOrWhiteSpace(request.BookingCode))
        {
            return new PaymentProcessResultDto
            {
                Success = false,
                Message = "Mã đơn đặt chỗ không được để trống."
            };
        }

        if (request.Amount <= 0)
        {
            return new PaymentProcessResultDto
            {
                Success = false,
                Message = "Số tiền thanh toán phải lớn hơn 0."
            };
        }

        var method = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "SANDBOX_SIMULATED" : request.PaymentMethod.Trim();
        var transactionCode = $"TXN-SANDBOX-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..32];

        return await _bookingRepository.ConfirmPaymentAndBookingAsync(
            request.BookingCode.Trim(),
            transactionCode,
            method,
            request.Amount,
            request.SimulateSuccess,
            request.FailureReason,
            ownerIdentifier,
            userId,
            ct);
    }

    public async Task<List<PaymentTransactionDto>> GetTransactionsByBookingCodeAsync(string bookingCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bookingCode)) return new List<PaymentTransactionDto>();

        var ownerIdentifier = await _bookingSessionContext.GetOwnerIdentifierAsync();
        var userId = await _bookingSessionContext.GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(ownerIdentifier) && !userId.HasValue) return new List<PaymentTransactionDto>();

        var transactions = await _bookingRepository.GetPaymentTransactionsByBookingCodeAsync(bookingCode.Trim(), ownerIdentifier, userId, ct);
        return transactions.Select(t => new PaymentTransactionDto
        {
            Id = t.Id,
            BookingId = t.BookingId,
            TransactionCode = t.TransactionCode,
            PaymentMethod = t.PaymentMethod,
            Amount = t.Amount,
            Status = t.Status.ToString(),
            ProviderReference = t.ProviderReference,
            Note = t.Note,
            IsSandbox = t.IsSandbox,
            CreatedAt = t.CreatedAt,
            CompletedAt = t.CompletedAt
        }).ToList();
    }
}
