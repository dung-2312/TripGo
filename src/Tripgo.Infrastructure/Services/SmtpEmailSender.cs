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

        if (IsTestDomain(confirmation.RecipientEmail))
        {
            _logger.LogInformation("Sandbox test recipient {Recipient} simulated successfully without hitting public SMTP.", confirmation.RecipientEmail);
            return Task.FromResult(true);
        }

        if (!Uri.TryCreate(_configuration["Email:BaseUrl"], UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("http" or "https"))
        {
            _logger.LogWarning("Booking confirmation email was not sent because Email:BaseUrl is missing or invalid.");
            return Task.FromResult(false);
        }

        var baseUrl = baseUri.ToString().TrimEnd('/');
        var seats = string.Join(", ", confirmation.SeatCodes);
        var departureTimeOffset = confirmation.DepartureTime.ToOffset(TimeSpan.FromHours(7));
        var departureFull = departureTimeOffset.ToString(
            "dddd, dd/MM/yyyy HH:mm",
            CultureInfo.GetCultureInfo("vi-VN"));
        var departureTimeStr = departureTimeOffset.ToString("HH:mm");
        var departureDateShort = departureTimeOffset.ToString("dd/MM");

        var amount = confirmation.TotalAmount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
        var template = BookingEmailTemplate.For(confirmation.MessageType, confirmation.BookingCode, confirmation.CancellationReason);
        var ticketCode = string.IsNullOrWhiteSpace(confirmation.TicketCode) ? null : confirmation.TicketCode;

        var (origin, dest) = ParseRoute(confirmation.RouteName);
        var manageOrderUrl = $"{baseUrl.TrimEnd('/')}/my-tickets?code={Uri.EscapeDataString(confirmation.BookingCode)}";
        var returnTripUrl = $"{baseUrl.TrimEnd('/')}/search-trips?origin={Uri.EscapeDataString(dest)}&dest={Uri.EscapeDataString(origin)}";
        var cancelOrderUrl = $"{baseUrl.TrimEnd('/')}/my-tickets?code={Uri.EscapeDataString(confirmation.BookingCode)}&action=cancel";
        var passengerPhone = string.IsNullOrWhiteSpace(confirmation.PassengerPhone) ? "Đang cập nhật" : confirmation.PassengerPhone;

        const string qrContentId = "ticket-qr";
        byte[]? qrBytes = null;
        try
        {
            using var generator = new QRCoder.QRCodeGenerator();
            if (ticketCode != null)
            {
                using var qrData = generator.CreateQrCode($"TRIPGO:{confirmation.BookingCode}:{ticketCode}", QRCoder.QRCodeGenerator.ECCLevel.M);
                var pngByteQr = new QRCoder.PngByteQRCode(qrData);
                qrBytes = pngByteQr.GetGraphic(5);
            }
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
            Mã vé điện tử (Ticket Code): {ticketCode ?? "Chưa được cấp"}
            Tuyến đường: {confirmation.RouteName}
            Nhà xe đối tác: {confirmation.OperatorName}
            Thời gian khởi hành: {departureFull}
            Vị trí ghế: {seats}
            Điểm đón: {confirmation.PickupPoint}
            Điểm trả: {confirmation.DropoffPoint}
            Tổng tiền: {amount} đ

            LIÊN KẾT THAO TÁC:
            - Quản lý đơn hàng: {manageOrderUrl}
            - Đặt vé chiều về: {returnTripUrl}
            - Hủy đơn hàng: {cancelOrderUrl}

            HƯỚNG DẪN XUẤT TRÌNH VÉ:
            Quý khách vui lòng có mặt tại điểm đón trước giờ khởi hành ít nhất 30 phút và xuất trình email này hoặc mã QR vé điện tử cho nhân viên nhà xe để soát vé lên xe.

            LƯU Ý SANDBOX: Đây là thông báo trong môi trường thử nghiệm (Sandbox). Thanh toán được mô phỏng, không phát sinh giao dịch tiền thật.
            """;

        var qrHtml = qrBytes is { Length: > 0 }
            ? $"""
                <div style="text-align:center;margin:18px 0 14px 0;padding:16px;background:#f8fafc;border-radius:12px;border:1px dashed #cbd5e1;">
                  <div style="font-size:12px;font-weight:bold;color:#475569;margin-bottom:8px;text-transform:uppercase;letter-spacing:0.05em;">Mã QR Vé điện tử</div>
                  <img src="cid:{qrContentId}" width="160" height="160" alt="Mã QR Vé điện tử" style="border:1px solid #e2e8f0;border-radius:8px;padding:6px;background:#ffffff;display:inline-block;"/>
                  <div style="font-size:13px;font-family:monospace;font-weight:bold;color:#0f172a;margin-top:8px;">{WebUtility.HtmlEncode(ticketCode)}</div>
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
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background-color:#f1f5f9;padding:16px 8px;">
                <tr>
                  <td align="center">
                    <div style="text-align:center;padding:10px 16px;color:#64748b;font-size:13px;max-width:600px;">
                      Đây là email tự động. Vui lòng không trả lời email này.
                    </div>
                    <table role="presentation" width="100%" style="max-width:600px;background:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 4px 6px -1px rgba(0,0,0,0.1),0 2px 4px -2px rgba(0,0,0,0.1);border-left:8px solid #3b82f6;border-right:8px solid #3b82f6;">
                      <!-- Vexere style Header -->
                      <tr>
                        <td style="background-color:#1868e8;padding:22px 24px;text-align:center;">
                          <div style="font-size:26px;font-weight:900;color:#ffffff;letter-spacing:0.04em;display:inline-block;">
                            <span style="background:#fed049;color:#071F49;padding:2px 8px;border-radius:6px;font-size:18px;margin-right:6px;vertical-align:middle;">&#128652;</span>
                            <span style="color:#ffffff;">TRIP</span><span style="color:#fed049;">GO</span>
                          </div>
                          <div style="color:#bfdbfe;font-size:12px;margin-top:4px;font-weight:500;">Nền tảng đặt vé xe khách trực tuyến</div>
                        </td>
                      </tr>

                      <!-- Welcome & Intro -->
                      <tr>
                        <td style="padding:24px 24px 8px 24px;">
                          <h1 style="font-size:22px;font-weight:800;color:#0f172a;margin:0 0 12px 0;">{WebUtility.HtmlEncode(template.Heading)}</h1>
                          <p style="margin:0 0 8px 0;font-size:15px;color:#1e293b;">Xin chào <strong>{WebUtility.HtmlEncode(confirmation.PassengerName)}</strong>,</p>
                          <p style="margin:0 0 8px 0;font-size:14px;color:#334155;line-height:1.6;">
                            Cảm ơn quý khách đã tin tưởng sử dụng dịch vụ của <strong>TripGo</strong>. TripGo xác nhận quý khách đã đặt chỗ thành công đơn hàng xe khách của nhà xe <strong>{WebUtility.HtmlEncode(confirmation.OperatorName)}</strong>.
                          </p>
                          <div style="background:#fffbeb;border:1px solid #fde68a;border-radius:8px;padding:12px 14px;margin:16px 0;font-size:13px;line-height:1.55;color:#92400e;">
                            <strong>Cảnh báo:</strong> TripGo <strong>KHÔNG</strong> bao giờ yêu cầu quý khách truy cập liên kết lạ, cung cấp mã OTP ngân hàng hoặc chuyển tiền vào tài khoản cá nhân không đứng tên công ty. Quý khách vui lòng nâng cao cảnh giác bảo mật.
                          </div>
                        </td>
                      </tr>

                      <!-- Trip Details & Schedule -->
                      <tr>
                        <td style="padding:8px 24px;">
                          <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;border-bottom:1px dashed #cbd5e1;padding-bottom:10px;">
                            <span style="font-size:13px;color:#64748b;">Mã ĐH: <strong style="color:#1868e8;font-family:monospace;font-size:15px;">{WebUtility.HtmlEncode(confirmation.BookingCode)}</strong></span>
                            <span style="font-size:13px;color:#64748b;">Mã vé: <strong style="color:#0f172a;font-family:monospace;">{WebUtility.HtmlEncode(ticketCode)}</strong></span>
                          </div>

                          <!-- Route Timeline Card -->
                          <div style="background:#f8fafc;border:1px solid #e2e8f0;border-radius:10px;padding:16px;margin-bottom:16px;">
                            <table width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;">
                              <tr>
                                <td style="width:75px;vertical-align:top;padding-bottom:16px;">
                                  <div style="font-size:18px;font-weight:800;color:#0f172a;line-height:1.2;">{departureTimeStr}</div>
                                  <div style="font-size:11px;color:#64748b;">({departureDateShort})</div>
                                </td>
                                <td style="border-left:2px solid #3b82f6;padding-left:14px;padding-bottom:16px;vertical-align:top;">
                                  <div style="font-size:14px;font-weight:700;color:#0f172a;">{WebUtility.HtmlEncode(confirmation.PickupPoint)}</div>
                                  <div style="font-size:12px;color:#64748b;margin-top:2px;">Điểm đón khách</div>
                                </td>
                              </tr>
                              <tr>
                                <td style="width:75px;vertical-align:top;">
                                  <div style="font-size:12px;font-weight:700;color:#64748b;line-height:1.2;">Chưa xác minh</div>
                                </td>
                                <td style="border-left:2px solid #ef4444;padding-left:14px;vertical-align:top;">
                                  <div style="font-size:14px;font-weight:700;color:#0f172a;">{WebUtility.HtmlEncode(confirmation.DropoffPoint)}</div>
                                  <div style="font-size:12px;color:#64748b;margin-top:2px;">Điểm trả khách (dự kiến)</div>
                                </td>
                              </tr>
                            </table>
                            <div style="font-size:11px;color:#94a3b8;margin-top:12px;border-top:1px dashed #e2e8f0;padding-top:8px;">
                              (*) Thời gian đón/trả là dự kiến và có thể thay đổi do một số điều kiện khách quan.
                            </div>
                          </div>

                          <!-- Vehicle details info -->
                          <table width="100%" cellpadding="8" cellspacing="0" style="font-size:13px;border-collapse:collapse;background:#ffffff;border:1px solid #e2e8f0;border-radius:8px;margin-bottom:16px;">
                            <tr style="border-bottom:1px solid #f1f5f9;">
                              <td style="color:#64748b;width:35%;">Nhà xe</td>
                              <td style="font-weight:700;color:#0f172a;">{WebUtility.HtmlEncode(confirmation.OperatorName)}</td>
                            </tr>
                            <tr style="border-bottom:1px solid #f1f5f9;">
                              <td style="color:#64748b;">Ghế / giường</td>
                              <td style="font-weight:700;color:#1868e8;">{WebUtility.HtmlEncode(seats)}</td>
                            </tr>
                            <tr>
                              <td style="color:#64748b;">Tổng tiền thanh toán</td>
                              <td style="font-size:16px;font-weight:800;color:#059669;">{WebUtility.HtmlEncode(amount)} đ</td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <!-- Operator Contact Card -->
                      <tr>
                        <td style="padding:0 24px 16px 24px;">
                          <div style="border:1px solid #e2e8f0;border-radius:10px;overflow:hidden;">
                            <div style="background:#f0f7ff;padding:10px 14px;font-weight:700;font-size:13px;color:#071F49;">
                              Thông tin liên hệ nhà xe
                            </div>
                            <table width="100%" cellpadding="8" cellspacing="0" style="font-size:13px;border-collapse:collapse;">
                              <tr style="border-bottom:1px solid #f1f5f9;"><td style="color:#64748b;width:40%;">Biển số xe</td><td style="font-weight:600;color:#0f172a;">Đang cập nhật</td></tr>
                              <tr style="border-bottom:1px solid #f1f5f9;"><td style="color:#64748b;">SĐT phụ xe</td><td style="font-weight:600;color:#0f172a;">Đang cập nhật</td></tr>
                              <tr style="border-bottom:1px solid #f1f5f9;"><td style="color:#64748b;">SĐT tài xế</td><td style="font-weight:600;color:#0f172a;">Đang cập nhật</td></tr>
                              <tr style="border-bottom:1px solid #f1f5f9;"><td style="color:#64748b;">SĐT văn phòng</td><td style="font-weight:600;color:#0f172a;">Chưa được xác minh</td></tr>
                              <tr><td style="color:#64748b;">Hotline hỗ trợ TripGo</td><td style="font-weight:600;color:#1868e8;">Chưa có kênh được xác minh</td></tr>
                            </table>
                          </div>
                        </td>
                      </tr>

                      <!-- Passenger Contact Card -->
                      <tr>
                        <td style="padding:0 24px 16px 24px;">
                          <div style="border:1px solid #e2e8f0;border-radius:10px;overflow:hidden;">
                            <div style="background:#f0f7ff;padding:10px 14px;font-weight:700;font-size:13px;color:#071F49;">
                              Thông tin liên hệ hành khách
                            </div>
                            <table width="100%" cellpadding="8" cellspacing="0" style="font-size:13px;border-collapse:collapse;">
                              <tr style="border-bottom:1px solid #f1f5f9;"><td style="color:#64748b;width:40%;">Họ tên</td><td style="font-weight:700;color:#0f172a;">{WebUtility.HtmlEncode(confirmation.PassengerName)}</td></tr>
                              <tr style="border-bottom:1px solid #f1f5f9;"><td style="color:#64748b;">Điện thoại</td><td style="font-weight:700;color:#0f172a;">{WebUtility.HtmlEncode(passengerPhone)}</td></tr>
                              <tr><td style="color:#64748b;">Email</td><td><a href="mailto:{confirmation.RecipientEmail}" style="color:#1868e8;text-decoration:none;font-weight:600;">{WebUtility.HtmlEncode(confirmation.RecipientEmail)}</a></td></tr>
                            </table>
                          </div>
                        </td>
                      </tr>

                      <!-- Action Buttons (Quản lý đơn hàng & Đặt chiều về) -->
                      <tr>
                        <td style="padding:8px 24px 16px 24px;">
                          <table width="100%" cellpadding="0" cellspacing="0">
                            <tr>
                              <td style="padding-bottom:12px;">
                                <a href="{manageOrderUrl}" target="_blank" style="display:block;background-color:#fed049;color:#0f172a;font-weight:bold;font-size:16px;text-decoration:none;padding:14px 20px;border-radius:8px;text-align:center;box-shadow:0 2px 4px rgba(0,0,0,0.06);">
                                  Quản lý đơn hàng
                                </a>
                              </td>
                            </tr>
                            <tr>
                              <td style="padding-bottom:8px;">
                                <a href="{returnTripUrl}" target="_blank" style="display:block;background-color:#fed049;color:#0f172a;font-weight:bold;font-size:16px;text-decoration:none;padding:14px 20px;border-radius:8px;text-align:center;box-shadow:0 2px 4px rgba(0,0,0,0.06);">
                                  Đặt chiều về
                                </a>
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <!-- Cancellation Policy & Cancel Button -->
                      <tr>
                        <td style="padding:0 24px 20px 24px;">
                          <div style="border:1px solid #e2e8f0;border-radius:10px;overflow:hidden;">
                            <div style="background:#f0f7ff;padding:10px 14px;font-weight:700;font-size:13px;color:#071F49;">
                              Chính sách hủy của nhà xe
                            </div>
                            <div style="padding:14px;">
                              <table width="100%" cellpadding="6" cellspacing="0" style="font-size:13px;border-collapse:collapse;margin-bottom:10px;">
                                <thead>
                                  <tr style="border-bottom:1px solid #cbd5e1;">
                                    <th align="left" style="color:#64748b;font-weight:600;padding-bottom:6px;">Thời gian huỷ</th>
                                    <th align="right" style="color:#64748b;font-weight:600;padding-bottom:6px;">Phí huỷ</th>
                                  </tr>
                                </thead>
                                <tbody>
                                  <tr>
                                    <td style="color:#0f172a;padding:8px 0;border-bottom:1px solid #f1f5f9;">Trước 12:00 giờ khởi hành</td>
                                    <td align="right" style="font-weight:700;color:#059669;padding:8px 0;border-bottom:1px solid #f1f5f9;">Miễn phí</td>
                                  </tr>
                                  <tr>
                                    <td style="color:#0f172a;padding:8px 0;">Trong vòng 12:00 giờ khởi hành</td>
                                    <td align="right" style="font-weight:700;color:#ef4444;padding:8px 0;">100%</td>
                                  </tr>
                                </tbody>
                              </table>
                              <div style="background:#eff6ff;border:1px solid #bfdbfe;border-radius:6px;padding:8px 12px;font-size:12px;color:#1e40af;line-height:1.5;margin-bottom:8px;">
                                Phí hủy được tính trên giá gốc (*) và không vượt quá số tiền quý khách đã thanh toán.
                              </div>
                              <div style="font-size:11px;color:#64748b;line-height:1.4;margin-bottom:14px;">
                                (*) Giá gốc là giá sau khuyến mãi, đã tính phụ thu đón trả hoặc suất ăn (nếu có) và chưa áp dụng mã giảm giá.
                              </div>
                              <a href="{cancelOrderUrl}" target="_blank" style="display:block;background-color:#ef4444;color:#ffffff;font-weight:bold;font-size:15px;text-decoration:none;padding:12px 20px;border-radius:8px;text-align:center;box-shadow:0 2px 4px rgba(239,68,68,0.2);">
                                Hủy đơn hàng
                              </a>
                            </div>
                          </div>
                        </td>
                      </tr>

                      <!-- QR Code Section -->
                      <tr>
                        <td style="padding:0 24px;">
                          {qrHtml}
                        </td>
                      </tr>

                      <!-- Boarding Instructions -->
                      <tr>
                        <td style="padding:0 24px 16px 24px;">
                          <div style="background:#eff6ff;border-left:4px solid #1868e8;padding:12px 14px;border-radius:4px;font-size:13px;color:#1e3a8a;line-height:1.5;">
                             <strong>Lưu ý thử nghiệm:</strong> Dữ liệu đặt chỗ và mã QR (nếu có) chỉ phục vụ kiểm thử kỹ thuật, không có giá trị để lên xe thật.
                          </div>
                        </td>
                      </tr>

                      <!-- Sandbox Notice -->
                      <tr>
                        <td style="padding:0 24px 20px 24px;">
                          <div style="background:#fffbeb;border:1px solid #fef3c7;padding:10px 14px;border-radius:6px;font-size:12px;color:#92400e;">
                            <strong>LƯU Ý THỬ NGHIỆM (SANDBOX):</strong> Email và mã vé này được tạo trong môi trường mô phỏng thử nghiệm của TripGo. Giao dịch không phát sinh chi phí tiền thật.
                          </div>
                        </td>
                      </tr>

                      <!-- Footer -->
                      <tr>
                        <td style="background:#f8fafc;padding:18px 24px;text-align:center;border-top:1px solid #e2e8f0;font-size:12px;color:#64748b;">
                          <div>Cảm ơn quý khách đã tin tưởng và đồng hành cùng <strong>TripGo</strong>.</div>
                           <div style="margin-top:4px;">TripGo hiện chưa có kênh hỗ trợ được xác minh.</div>
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

    private static (string origin, string dest) ParseRoute(string routeName)
    {
        if (string.IsNullOrWhiteSpace(routeName)) return ("Hà Nội", "Sa Pa");
        var parts = routeName.Split(new[] { '-', '→', '–' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return (parts[0], parts[1]);
        }
        return (routeName, routeName);
    }

    private static bool IsTestDomain(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        return email.EndsWith(".test", StringComparison.OrdinalIgnoreCase) ||
               email.EndsWith(".example", StringComparison.OrdinalIgnoreCase) ||
               email.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase) ||
               email.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
               email.EndsWith("@example.com", StringComparison.OrdinalIgnoreCase) ||
               email.EndsWith("@test.com", StringComparison.OrdinalIgnoreCase);
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

        var fromDisplayName = _configuration["Email:FromName"] ?? "TripGo - Đặt Vé Xe Khách";
        var message = new MailMessage
        {
            From = new MailAddress(fromAddress, fromDisplayName, Encoding.UTF8),
            Subject = subject,
            Body = textBody,
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(email));

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
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(GetSendTimeout());
            await client.SendMailAsync(message, timeout.Token);
        }

        return true;
    }
}
