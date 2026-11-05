using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Globalization;
using Tripgo.Application.Interfaces;
using Tripgo.Application.DTOs;

namespace Tripgo.Infrastructure.Services;

public sealed class SmtpEmailSender : IEmailSender
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(90);
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(
        IConfiguration configuration,
        ILogger<SmtpEmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> SendPasswordResetAsync(
        string email,
        string token,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = _configuration["Email:BaseUrl"];

        if (!TryGetApplicationUri(baseUrl, out var applicationUri))
        {
            _logger.LogWarning("Password reset email was not sent because Email:BaseUrl is missing or invalid.");
            return false;
        }

        var resetUrl = $"{applicationUri.ToString().TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(token)}";
        var encodedUrl = WebUtility.HtmlEncode(resetUrl);
        return await SendAsync(
            email,
            "Đặt lại mật khẩu TripGo",
            $"Mở liên kết sau để đặt lại mật khẩu (liên kết hết hạn sau 15 phút):\r\n{resetUrl}",
            $"<p>Chúng tôi đã nhận yêu cầu đặt lại mật khẩu TripGo.</p><p><a href=\"{encodedUrl}\">Đặt lại mật khẩu</a></p><p>Liên kết hết hạn sau 15 phút. Nếu bạn không yêu cầu, hãy bỏ qua email này.</p>",
            cancellationToken);
    }

    public Task<bool> SendBookingConfirmationAsync(
        BookingConfirmationEmailDto confirmation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        if (string.IsNullOrWhiteSpace(confirmation.RecipientEmail) ||
            string.IsNullOrWhiteSpace(confirmation.BookingCode))
        {
            throw new ArgumentException("Booking confirmation requires a recipient and booking code.", nameof(confirmation));
        }

        var seats = string.Join(", ", confirmation.SeatCodes);
        var departure = confirmation.DepartureTime.ToLocalTime().ToString(
            "dddd, dd/MM/yyyy HH:mm",
            CultureInfo.GetCultureInfo("vi-VN"));
        var amount = confirmation.TotalAmount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
        var text = $"""
            Xin chào {confirmation.PassengerName},

            Mã đặt chỗ thử nghiệm: {confirmation.BookingCode}
            Tuyến tham khảo: {confirmation.RouteName}
            Nhà xe: {confirmation.OperatorName}
            Khởi hành tham khảo: {departure}
            Ghế mô phỏng: {seats}
            Điểm đón: {confirmation.PickupPoint}
            Điểm trả: {confirmation.DropoffPoint}
            Số tiền hiển thị: {amount} đ

            Đây là xác nhận SANDBOX. Thanh toán được mô phỏng, không phát sinh giao dịch tiền thật. Đây không phải vé xe thương mại, không xác nhận chỗ thật và không có mã QR lên xe.
            """;

        var html = $"""
            <!doctype html>
            <html lang="vi">
            <body style="font-family:Arial,sans-serif;color:#172033;line-height:1.55">
              <main style="max-width:640px;margin:auto;padding:24px">
                <h1 style="font-size:22px">Thông tin đặt chỗ TripGo (Sandbox)</h1>
                <p>Xin chào {WebUtility.HtmlEncode(confirmation.PassengerName)},</p>
                <p>Mã đặt chỗ thử nghiệm: <strong>{WebUtility.HtmlEncode(confirmation.BookingCode)}</strong></p>
                <table style="border-collapse:collapse;width:100%">
                  <tr><th align="left">Tuyến tham khảo</th><td>{WebUtility.HtmlEncode(confirmation.RouteName)}</td></tr>
                  <tr><th align="left">Nhà xe</th><td>{WebUtility.HtmlEncode(confirmation.OperatorName)}</td></tr>
                  <tr><th align="left">Khởi hành tham khảo</th><td>{WebUtility.HtmlEncode(departure)}</td></tr>
                  <tr><th align="left">Ghế mô phỏng</th><td>{WebUtility.HtmlEncode(seats)}</td></tr>
                  <tr><th align="left">Điểm đón</th><td>{WebUtility.HtmlEncode(confirmation.PickupPoint)}</td></tr>
                  <tr><th align="left">Điểm trả</th><td>{WebUtility.HtmlEncode(confirmation.DropoffPoint)}</td></tr>
                  <tr><th align="left">Số tiền hiển thị</th><td>{WebUtility.HtmlEncode(amount)} đ</td></tr>
                </table>
                <p style="margin-top:24px;padding:14px;background:#fff3cd">
                  <strong>SANDBOX:</strong> Thanh toán được mô phỏng, không phát sinh giao dịch tiền thật.
                  Đây không phải vé xe thương mại, không xác nhận chỗ thật và không có mã QR lên xe.
                </p>
              </main>
            </body>
            </html>
            """;

        return SendAsync(
            confirmation.RecipientEmail,
            $"Thông tin đặt chỗ TripGo Sandbox - {confirmation.BookingCode}",
            text,
            html,
            cancellationToken);
    }

    private bool TryGetSmtpSettings(
        out string host,
        out int port,
        out string username,
        out string password,
        out string fromAddress)
    {
        host = _configuration["Email:Smtp:Host"] ?? string.Empty;
        port = 0;
        username = _configuration["Email:Smtp:Username"] ?? string.Empty;
        password = _configuration["Email:Smtp:Password"] ?? string.Empty;
        fromAddress = _configuration["Email:FromAddress"] ?? string.Empty;
        return !string.IsNullOrWhiteSpace(host) &&
            !string.IsNullOrWhiteSpace(username) &&
            !string.IsNullOrWhiteSpace(password) &&
            !string.IsNullOrWhiteSpace(fromAddress) &&
            int.TryParse(_configuration["Email:Smtp:Port"], out port) &&
            port is > 0 and <= 65535;
    }

    private static bool TryGetApplicationUri(string? baseUrl, out Uri applicationUri)
    {
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttps || parsed.IsLoopback) &&
            parsed.AbsolutePath == "/" &&
            string.IsNullOrEmpty(parsed.Query) &&
            string.IsNullOrEmpty(parsed.Fragment))
        {
            applicationUri = parsed;
            return true;
        }

        applicationUri = null!;
        return false;
    }

    private async Task<bool> SendAsync(
        string email,
        string subject,
        string textBody,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        if (!TryGetSmtpSettings(
                out var host,
                out var port,
                out var username,
                out var password,
                out var fromAddress))
        {
            _logger.LogWarning("Email was not sent because SMTP settings are incomplete.");
            return false;
        }

        var message = new MailMessage
        {
            From = new MailAddress(fromAddress, _configuration["Email:FromName"] ?? "TripGo"),
            Subject = subject,
            Body = textBody,
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(email));
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            htmlBody,
            Encoding.UTF8,
            "text/html"));

        using (message)
        using (var client = new SmtpClient(host, port)
        {
            EnableSsl = !bool.TryParse(_configuration["Email:Smtp:EnableSsl"], out var enableSsl) || enableSsl,
            Credentials = new NetworkCredential(username, password)
        })
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SendTimeout);
            await client.SendMailAsync(message, timeout.Token);
        }

        return true;
    }
}
