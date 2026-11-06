using System.Security.Cryptography;
using System.Text;
using Tripgo.Application.Interfaces;
using Tripgo.Application.Interfaces.Persistence;
using Tripgo.Domain.Entities;

namespace Tripgo.Application.Services;

public sealed class TicketService : ITicketService
{
    private const string TokenPrefix = "tgt_";
    private const int MaxTokenLength = 100;
    public const int MaxAttemptsPerMinute = 30;
    private static readonly TimeSpan ValidityAfterArrival = TimeSpan.FromHours(6);

    private readonly ITicketRepository _repository;
    private readonly IBookingSessionContext _session;
    private readonly IAdminAccessGuard _guard;
    private readonly ITicketTokenProtector _protector;

    public TicketService(
        ITicketRepository repository,
        IBookingSessionContext session,
        IAdminAccessGuard guard,
        ITicketTokenProtector protector)
    {
        _repository = repository;
        _session = session;
        _guard = guard;
        _protector = protector;
    }

    public async Task<MyTicketDto> GetMyTicketAsync(string bookingCode, CancellationToken ct = default)
    {
        var owner = await _session.GetOwnerIdentifierAsync();
        var userId = await _session.GetUserIdAsync();
        var booking = await _repository.GetOwnedBookingAsync(bookingCode?.Trim() ?? string.Empty, owner, userId, ct);
        if (booking == null)
        {
            return new MyTicketDto { Message = "Không tìm thấy đơn đặt chỗ hoặc bạn không có quyền xem." };
        }

        if (!booking.IsConfirmedAndPaid)
        {
            return new MyTicketDto { Message = "Mã kiểm tra chỉ được tạo sau khi đơn đã thanh toán và xác nhận." };
        }

        var now = DateTimeOffset.UtcNow;
        var expiresAt = booking.ExpectedArrival.Add(ValidityAfterArrival);
        if (expiresAt <= now)
        {
            return new MyTicketDto
            {
                Message = "Vé đã hết hạn và không thể cấp lại sau thời gian hiệu lực của chuyến."
            };
        }

        var credential = await IssueAsync(booking, expiresAt, now, ct);
        var token = _protector.Unprotect(credential.ProtectedToken);
        if (token == null)
        {
            return new MyTicketDto { Message = "Không thể hiển thị mã kiểm tra lúc này. Vui lòng liên hệ hỗ trợ." };
        }

        return new MyTicketDto
        {
            Available = booking.IsConfirmedAndPaid,
            Message = booking.IsConfirmedAndPaid ? string.Empty : "Đơn đặt chỗ không còn hiệu lực.",
            Token = booking.IsConfirmedAndPaid ? token : null,
            IsSandbox = credential.IsSandbox,
            ExpiresAt = credential.ExpiresAt,
            BoardedAt = credential.BoardedAt,
            RevokedAt = credential.RevokedAt
        };
    }

    private async Task<TicketCredential> IssueAsync(
        OwnedBookingForTicket booking,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var token = TokenPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var credential = new TicketCredential
        {
            BookingId = booking.BookingId,
            TokenHash = HashToken(token),
            ProtectedToken = _protector.Protect(token),
            IsSandbox = booking.IsSandbox,
            IssuedAt = now,
            ExpiresAt = expiresAt
        };

        return await _repository.GetOrIssueCredentialAsync(credential, now, ct)
            ?? throw new InvalidOperationException("The ticket credential could not be issued.");
    }

    public Task<TicketVerificationDto> VerifyAsync(string token, CancellationToken ct = default) =>
        CheckAsync(token, board: false, ct);

    public Task<TicketVerificationDto> BoardAsync(string token, CancellationToken ct = default) =>
        CheckAsync(token, board: true, ct);

    private async Task<TicketVerificationDto> CheckAsync(string token, bool board, CancellationToken ct)
    {
        var actor = await _guard.RequireRoleAsync(RoleNames.Admin, RoleNames.Operator);
        var actorKey = $"user:{actor.UserId}";
        var now = DateTimeOffset.UtcNow;

        var normalized = token?.Trim() ?? string.Empty;
        var hash = HashToken(normalized);
        var fingerprint = hash[..16];

        if (await _repository.CountRecentAttemptsAsync(actorKey, now.AddMinutes(-1), ct) >= MaxAttemptsPerMinute)
        {
            await RecordAsync(null, fingerprint, TicketVerificationResult.RateLimited, actor, actorKey, now, ct);
            return Result(TicketVerificationResult.RateLimited, "Quá nhiều lần kiểm tra. Vui lòng đợi một phút.");
        }

        if (!normalized.StartsWith(TokenPrefix, StringComparison.Ordinal) || normalized.Length > MaxTokenLength)
        {
            await RecordAsync(null, fingerprint, TicketVerificationResult.InvalidToken, actor, actorKey, now, ct);
            return Result(TicketVerificationResult.InvalidToken, "Mã không hợp lệ.");
        }

        var context = await _repository.GetByTokenHashAsync(hash, ct);
        if (context == null)
        {
            await RecordAsync(null, fingerprint, TicketVerificationResult.InvalidToken, actor, actorKey, now, ct);
            return Result(TicketVerificationResult.InvalidToken, "Mã không hợp lệ.");
        }

        var credential = context.Credential;
        var (result, message) = Evaluate(context, actor, now);

        if (result == TicketVerificationResult.Valid && board)
        {
            if (await _repository.TryMarkBoardedAsync(credential.Id, actor.UserId, now, ct))
            {
                result = TicketVerificationResult.Boarded;
                message = credential.IsSandbox ? "Đã ghi nhận kiểm tra mã SANDBOX. Đây không phải vé thương mại." : "Đã ghi nhận lên xe.";
            }
            else
            {
                result = TicketVerificationResult.AlreadyBoarded;
                message = "Mã này đã được sử dụng.";
            }
        }

        await RecordAsync(credential.Id, fingerprint, result, actor, actorKey, now, ct);

        // A rejected operator learns nothing about another operator's passengers.
        if (result == TicketVerificationResult.WrongOperator)
        {
            return Result(result, message);
        }

        return new TicketVerificationDto
        {
            Result = result,
            Message = message,
            IsSandbox = credential.IsSandbox,
            BookingCode = context.BookingCode,
            RouteName = context.RouteName,
            DepartureTime = context.DepartureTime,
            SeatCodes = context.SeatCodes,
            MaskedPassengerName = MaskName(context.PassengerName),
            BoardedAt = result == TicketVerificationResult.Boarded ? now : credential.BoardedAt
        };
    }

    private static (TicketVerificationResult Result, string Message) Evaluate(TicketContext context, AdminActor actor, DateTimeOffset now)
    {
        var credential = context.Credential;
        if (!actor.IsAdmin && actor.OperatorId != context.OperatorId)
            return (TicketVerificationResult.WrongOperator, "Vé này không thuộc nhà xe của bạn.");
        if (credential.RevokedAt != null)
            return (TicketVerificationResult.Revoked, "Mã đã bị thu hồi.");
        if (context.TripCancelled)
            return (TicketVerificationResult.TripCancelled, "Chuyến xe đã bị hủy.");
        if (!context.BookingConfirmed)
            return (TicketVerificationResult.BookingNotConfirmed, "Đơn đặt chỗ không còn hiệu lực.");
        if (credential.BoardedAt != null)
            return (TicketVerificationResult.AlreadyBoarded, "Mã này đã được sử dụng.");
        if (credential.ExpiresAt <= now)
            return (TicketVerificationResult.Expired, "Mã đã hết hạn.");

        return (TicketVerificationResult.Valid,
            credential.IsSandbox
                ? "Mã hợp lệ trong môi trường SANDBOX. Không có giá trị lên xe thật."
                : "Mã hợp lệ.");
    }

    public async Task<bool> RevokeAsync(long bookingId, string reason, CancellationToken ct = default)
    {
        await _guard.RequireRoleAsync(RoleNames.Admin);
        return await _repository.RevokeLiveCredentialAsync(bookingId, reason, DateTimeOffset.UtcNow, ct) > 0;
    }

    private Task RecordAsync(long? credentialId, string fingerprint, TicketVerificationResult result, AdminActor actor, string actorKey, DateTimeOffset now, CancellationToken ct) =>
        _repository.AddAttemptAsync(new TicketVerificationAttempt
        {
            TicketCredentialId = credentialId,
            TokenFingerprint = fingerprint,
            Result = result.ToString(),
            ActorUserId = actor.UserId,
            ActorKey = actorKey,
            AttemptedAt = now
        }, ct);

    private static TicketVerificationDto Result(TicketVerificationResult result, string message) =>
        new() { Result = result, Message = message };

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public static string MaskName(string? name)
    {
        var parts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? string.Empty : string.Join(' ', parts.Select(part => part[0] + "***"));
    }
}
