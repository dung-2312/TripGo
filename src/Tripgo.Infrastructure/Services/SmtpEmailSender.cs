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
    private static readonly TimeSpan DefaultSendTimeout = TimeSpan.FromSeconds(60);
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
        var departure = confirmation.DepartureTime.ToOffset(TimeSpan.FromHours(7)).ToString(
            "dddd, dd/MM/yyyy HH:mm",
            CultureInfo.GetCultureInfo("vi-VN"));
        var amount = confirmation.TotalAmount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
        var template = BookingEmailTemplate.For(confirmation.MessageType, confirmation.BookingCode, confirmation.CancellationReason);
        var text = $"""
            Xin chào {confirmation.PassengerName},

            {template.Lead}

            Mã đặt chỗ thử nghiệm: {confirmation.BookingCode}
            Tuyến tham khảo: {confirmation.RouteName}
            Nhà xe: {confirmation.OperatorName}
            Khởi hành tham khảo: {departure}
            Ghế mô phỏng: {seats}
            Điểm đón: {confirmation.PickupPoint}
            Điểm trả: {confirmation.DropoffPoint}
            Số tiền hiển thị: {amount} đ

            Đây là thông báo SANDBOX. Thanh toán được mô phỏng, không phát sinh giao dịch tiền thật. Đây không phải vé xe thương mại và không xác nhận chỗ thật.
            """;

        var html = $"""
            <!doctype html>
            <html lang="vi">
            <body style="font-family:Arial,sans-serif;color:#172033;line-height:1.55">
              <main style="max-width:640px;margin:auto;padding:24px">
                <h1 style="font-size:22px">{WebUtility.HtmlEncode(template.Heading)}</h1>
                <p>Xin chào {WebUtility.HtmlEncode(confirmation.PassengerName)},</p>
                <p>{WebUtility.HtmlEncode(template.Lead)}</p>
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
                  Đây không phải vé xe thương mại và không xác nhận chỗ thật.
                </p>
              </main>
            </body>
            </html>
            """;

        return SendAsync(
            confirmation.RecipientEmail,
            template.Subject,
            text,
            html,
            cancellationToken,
            messageIdSeed: $"{confirmation.BookingCode}.{confirmation.MessageType}");
    }

    private TimeSpan GetSendTimeout() =>
        int.TryParse(_configuration["Email:Smtp:TimeoutSeconds"], out var seconds) && seconds is >= 5 and <= 100
            ? TimeSpan.FromSeconds(seconds)
            : DefaultSendTimeout;

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
        CancellationToken cancellationToken,
        string? messageIdSeed = null)
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

        // A stable ID lets receiving systems recognize a redelivery after a crash between
        // SMTP acceptance and our own "sent" record, which is the one duplicate risk an outbox cannot remove.
        if (!string.IsNullOrWhiteSpace(messageIdSeed) && Uri.TryCreate(_configuration["Email:BaseUrl"], UriKind.Absolute, out var messageIdHost))
        {
            message.Headers.Add("Message-ID", $"<{messageIdSeed}@{messageIdHost.Host}>");
        }
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            htmlBody,
            Encoding.UTF8,
            "text/html"));

        using (message)
        using (var client = new SmtpClient(host, port)
        {
            EnableSsl = !bool.TryParse(_configuration["Email:Smtp:EnableSsl"], out var enableSsl) || enableSsl,
            Credentials = new NetworkCredential(username, password),
            Timeout = (int)GetSendTimeout().TotalMilliseconds
        })
        {
            // This one limit covers connecting, the TLS handshake and sending. SendMailAsync ignores
            // SmtpClient.Timeout, so the cancellation token is what actually enforces it.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(GetSendTimeout());
            await client.SendMailAsync(message, timeout.Token);
        }

        return true;
    }
}
