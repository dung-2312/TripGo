using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Services;

public sealed record BookingEmailTemplate(string Subject, string Heading, string Lead)
{
    public static BookingEmailTemplate For(string? messageType, string bookingCode, string? cancellationReason) => messageType switch
    {
        BookingConfirmationEmailOutbox.TypeBookingAcknowledgement => new(
            $"Đã nhận đơn đặt chỗ TripGo Sandbox - {bookingCode}",
            "Đã nhận đơn đặt chỗ (Sandbox)",
            "Chúng tôi đã nhận đơn đặt chỗ của bạn. Đơn chưa được xác nhận cho đến khi thanh toán hoàn tất."),
        BookingConfirmationEmailOutbox.TypePaymentFailed => new(
            $"Thanh toán chưa thành công - {bookingCode}",
            "Thanh toán chưa thành công (Sandbox)",
            "Giao dịch thanh toán của bạn chưa thành công và chưa có khoản tiền nào được ghi nhận. Bạn có thể thử lại trước khi đơn hết hạn."),
        BookingConfirmationEmailOutbox.TypeBookingCancellation => new(
            $"Đơn đặt chỗ đã hủy - {bookingCode}",
            "Đơn đặt chỗ đã hủy (Sandbox)",
            string.IsNullOrWhiteSpace(cancellationReason)
                ? "Đơn đặt chỗ của bạn đã được hủy và ghế đã được trả lại."
                : $"Đơn đặt chỗ của bạn đã được hủy và ghế đã được trả lại. Lý do: {cancellationReason}"),
        _ => new(
            $"Thông tin đặt chỗ TripGo Sandbox - {bookingCode}",
            "Thông tin đặt chỗ TripGo (Sandbox)",
            "Đơn đặt chỗ của bạn đã được xác nhận trong môi trường thử nghiệm.")
    };
}
