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
        var ticketCode = string.IsNullOrWhiteSpace(confirmation.TicketCode)
            ? $"TKT-{confirmation.BookingCode}"
            : confirmation.TicketCode;

        const string qrContentId = "ticket-qr";
        byte[]? qrBytes = null;
        try
        {
            using var generator = new QRCoder.QRCodeGenerator();
            using var qrData = generator.CreateQrCode($"TRIPGO:{confirmation.BookingCode}:{ticketCode}", QRCoder.QRCodeGenerator.ECCLevel.M);
            var pngByteQr = new QRCoder.PngByteQRCode(qrData);
            qrBytes = pngByteQr.GetGraphic(5);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to render QR code for email confirmation {BookingCode}.", confirmation.BookingCode);
        }

        var text = $"""
            Xin chào {confirmation.PassengerName},

            {template.Lead}

            THÔNG TIN VÉ ĐIỆN TỬ:
            Mã đặt chỗ (Booking Code): {confirmation.BookingCode}
            Mã vé điện tử (Ticket Code): {ticketCode}
            Tuyến đường: {confirmation.RouteName}
            Nhà xe đối tác: {confirmation.OperatorName}
            Thời gian khởi hành: {departure}
            Vị trí ghế: {seats}
            Điểm đón: {confirmation.PickupPoint}
            Điểm trả: {confirmation.DropoffPoint}
            Tổng tiền: {amount} đ

            HƯỚNG DẪN XUẤT TRÌNH VÉ:
            Quý khách vui lòng có mặt tại điểm đón trước giờ khởi hành ít nhất 30 phút và xuất trình email này hoặc mã vé cho nhân viên nhà xe để soát vé lên xe.

            LƯU Ý SANDBOX: Đây là thông báo trong môi trường thử nghiệm (Sandbox). Thanh toán được mô phỏng, không phát sinh giao dịch tiền thật.
            """;

        var qrHtml = qrBytes is { Length: > 0 }
            ? $"""
                <div style="text-align:center;margin:24px 0 16px 0;padding:16px;background:#f8fafc;border-radius:12px;border:1px dashed #cbd5e1">
                  <div style="font-size:12px;font-weight:bold;color:#475569;margin-bottom:8px;text-transform:uppercase;letter-spacing:0.05em">Mã QR Vé điện tử</div>
                  <img src="cid:{qrContentId}" width="160" height="160" alt="Mã QR Vé điện tử" style="border:1px solid #e2e8f0;border-radius:8px;padding:6px;background:#ffffff;display:inline-block"/>
                  <div style="font-size:13px;font-family:monospace;font-weight:bold;color:#0f172a;margin-top:8px">{WebUtility.HtmlEncode(ticketCode)}</div>
                </div>
              """
            : string.Empty;

        var html = $"""
            <!doctype html>
            <html lang="vi">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1.0">
            </head>
            <body style="margin:0;padding:0;background-color:#f1f5f9;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:#1e293b;line-height:1.6">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background-color:#f1f5f9;padding:24px 12px">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" style="max-width:600px;background:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 4px 6px -1px rgba(0,0,0,0.1),0 2px 4px -2px rgba(0,0,0,0.1)">
                      <!-- Header -->
                      <tr>
                        <td style="background-color:#071F49;padding:24px;text-align:center">
                          <div style="font-size:24px;font-weight:bold;color:#ffffff;letter-spacing:0.05em">
                            <span style="color:#FFCA28">TRIP</span>GO
                          </div>
                          <div style="color:#94a3b8;font-size:13px;margin-top:4px">Nền tảng đặt vé xe khách trực tuyến</div>
                        </td>
                      </tr>
                      <!-- Title & Greeting -->
                      <tr>
                        <td style="padding:28px 28px 12px 28px">
                          <h1 style="font-size:20px;font-weight:bold;color:#071F49;margin:0 0 12px 0">{WebUtility.HtmlEncode(template.Heading)}</h1>
                          <p style="margin:0 0 8px 0;font-size:15px">Xin chào <strong>{WebUtility.HtmlEncode(confirmation.PassengerName)}</strong>,</p>
                          <p style="margin:0;font-size:14px;color:#475569">{WebUtility.HtmlEncode(template.Lead)}</p>
                        </td>
                      </tr>
                      <!-- Ticket Details Card -->
                      <tr>
                        <td style="padding:12px 28px">
                          <table role="presentation" width="100%" style="border-collapse:collapse;background:#f8fafc;border-radius:8px;border:1px solid #e2e8f0;overflow:hidden">
                            <tr>
                              <td style="padding:10px 16px;border-bottom:1px solid #e2e8f0;font-size:13px;color:#64748b;width:40%">Mã đặt chỗ (Booking)</td>
                              <td style="padding:10px 16px;border-bottom:1px solid #e2e8f0;font-size:14px;font-weight:bold;color:#0284c7;font-family:monospace">{WebUtility.HtmlEncode(confirmation.BookingCode)}</td>
                            </tr>
                            <tr>
                              <td style="padding:10px 16px;border-bottom:1px solid #e2e8f0;font-size:13px;color:#64748b">Mã vé điện tử</td>
                              <td style="padding:10px 16px;border-bottom:1px solid #e2e8f0;font-size:14px;font-weight:bold;color:#0f172a;font-family:monospace">{WebUtility.HtmlEncode(ticketCode)}</td>
                            </tr>
                            <tr>
                              <td style="padding:10px 16px;border-bottom:1px solid #e2e8f0;font-size:13px;color:#64748b">Tuyến đường</td>
                              <td style="padding:10px 16px;border-bottom:1px solid #e2e8f0;font-size:14px;font-weight:bold;color:#0f172a">{WebUtility.HtmlEncode(confirmation.RouteName)}</td>
                            </tr>
                            <tr>
                              <td style="padding:10px 16px;border-bottom:1px solid #e2e8f0;font-size:13px;color:#64748b">Nhà xe</td>
                              <td style="padding:10px 16px;border-bottom:1px solid #e2e8f0;font-size:14px;color:#0f172a">{WebUtility.HtmlEncode(confirmation.OperatorName)}</td>
                            </tr>
                            <tr>
                              <td style="padding:10px 16px;border-bottom:1px solid #e2e8f0;font-size:13px;color:#64748b">Thời gian khởi hành</td>
                              <td style="padding:10px 16px;border-bottom:1px solid #e2e8f0;font-size:14px;font-weight:bold;color:#071F49">{WebUtility.HtmlEncode(departure)}</td>
                            </tr>
                            <tr>
                              <td style="padding:10px 16px;border-bottom:1px solid #e2e8f0;font-size:13px;color:#64748b">Vị trí ghế</td>
                              <td style="padding:10px 16px;border-bottom:1px solid #e2e8f0;font-size:14px;font-weight:bold;color:#0284c7">{WebUtility.HtmlEncode(seats)}</td>
                            </tr>
                            <tr>
                              <td style="padding:10px 16px;border-bottom:1px solid #e2e8f0;font-size:13px;color:#64748b">Điểm đón</td>
                              <td style="padding:10px 16px;border-bottom:1px solid #e2e8f0;font-size:13px;color:#0f172a">{WebUtility.HtmlEncode(confirmation.PickupPoint)}</td>
                            </tr>
                            <tr>
                              <td style="padding:10px 16px;border-bottom:1px solid #e2e8f0;font-size:13px;color:#64748b">Điểm trả</td>
                              <td style="padding:10px 16px;border-bottom:1px solid #e2e8f0;font-size:13px;color:#0f172a">{WebUtility.HtmlEncode(confirmation.DropoffPoint)}</td>
                            </tr>
                            <tr>
                              <td style="padding:12px 16px;font-size:14px;font-weight:bold;color:#0f172a">Tổng tiền</td>
                              <td style="padding:12px 16px;font-size:16px;font-weight:bold;color:#dc2626">{WebUtility.HtmlEncode(amount)} đ</td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                      <!-- QR Code Section -->
                      <tr>
                        <td style="padding:0 28px">
                          {qrHtml}
                        </td>
                      </tr>
                      <!-- Boarding Instructions -->
                      <tr>
                        <td style="padding:8px 28px 16px 28px">
                          <div style="background:#eff6ff;border-left:4px solid #3b82f6;padding:12px 16px;border-radius:4px;font-size:13px;color:#1e3a8a">
                            <strong>Hướng dẫn xuất trình vé:</strong> Quý khách vui lòng có mặt tại điểm đón trước giờ khởi hành ít nhất <strong>30 phút</strong> và xuất trình email này hoặc mã QR vé điện tử cho nhân viên nhà xe để soát vé lên xe.
                          </div>
                        </td>
                      </tr>
                      <!-- Sandbox Disclaimer -->
                      <tr>
                        <td style="padding:0 28px 24px 28px">
                          <div style="background:#fffbeb;border:1px solid #fef3c7;padding:10px 14px;border-radius:6px;font-size:12px;color:#92400e">
                            <strong>LƯU Ý THỬ NGHIỆM (SANDBOX):</strong> Email và mã vé này được tạo trong môi trường mô phỏng thử nghiệm của TripGo. Giao dịch không phát sinh chi phí tiền thật và không thay thế vé xe khách thật.
                          </div>
                        </td>
                      </tr>
                      <!-- Footer -->
                      <tr>
                        <td style="background:#f8fafc;padding:20px;text-align:center;border-top:1px solid #e2e8f0;font-size:12px;color:#64748b">
                          <div>Cảm ơn quý khách đã tin tưởng và đồng hành cùng <strong>TripGo</strong>.</div>
                          <div style="margin-top:4px">Hotline hỗ trợ: 1900 6868 | Email: hotro@tripgo.vn</div>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        return SendAsync(
            confirmation.RecipientEmail,
            template.Subject,
            text,
            html,
            cancellationToken,
            messageIdSeed: $"{confirmation.BookingCode}.{confirmation.MessageType}",
            inlineImageBytes: qrBytes,
            inlineImageContentId: qrBytes is { Length: > 0 } ? qrContentId : null);
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
        string? messageIdSeed = null,
        byte[]? inlineImageBytes = null,
        string? inlineImageContentId = null)
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

        var htmlView = AlternateView.CreateAlternateViewFromString(
            htmlBody,
            Encoding.UTF8,
            "text/html");

        MemoryStream? imageStream = null;
        if (inlineImageBytes is { Length: > 0 } && !string.IsNullOrWhiteSpace(inlineImageContentId))
        {
            imageStream = new MemoryStream(inlineImageBytes);
            var resource = new LinkedResource(imageStream, "image/png")
            {
                ContentId = inlineImageContentId,
                TransferEncoding = System.Net.Mime.TransferEncoding.Base64
            };
            htmlView.LinkedResources.Add(resource);
        }

        message.AlternateViews.Add(htmlView);

        using (message)
        using (imageStream)
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
