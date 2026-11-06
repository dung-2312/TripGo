using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tripgo.Application.Configuration;
using Tripgo.Application.DTOs;
using Tripgo.Application.Interfaces;
using Tripgo.Application.Interfaces.Persistence;
using Tripgo.Domain.Entities;

namespace Tripgo.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly IPasswordHasherService _passwordHasher;
    private readonly IEmailSender? _emailSender;
    private readonly ILogger<AuthService>? _logger;
    private readonly IPasswordResetTokenRepository? _passwordResetTokenRepository;
    private readonly AuthSecurityOptions _securityOptions;
    private readonly IAuthRateLimiter _rateLimiter;
    private readonly IAuthClientAddressProvider _clientAddressProvider;

    private const string InvalidLoginMessage = "Email, số điện thoại hoặc mật khẩu không chính xác. Nếu đăng nhập thất bại nhiều lần, vui lòng thử lại sau.";
    private const string ResetRequestAcceptedMessage = "Nếu email khớp với tài khoản và dịch vụ email đã được cấu hình, liên kết đặt lại sẽ được gửi.";
    private const string InvalidResetTokenMessage = "Mã đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.";
    private static readonly string DummyPasswordHash = new PasswordHasherService().HashPassword(
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    public AuthService(
        IUserRepository userRepository,
        IBookingRepository bookingRepository,
        IPasswordHasherService passwordHasher,
        IEmailSender? emailSender = null,
        ILogger<AuthService>? logger = null,
        IPasswordResetTokenRepository? passwordResetTokenRepository = null,
        AuthSecurityOptions? securityOptions = null,
        IAuthRateLimiter? rateLimiter = null,
        IAuthClientAddressProvider? clientAddressProvider = null)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _bookingRepository = bookingRepository ?? throw new ArgumentNullException(nameof(bookingRepository));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _emailSender = emailSender;
        _logger = logger;
        _passwordResetTokenRepository = passwordResetTokenRepository;
        _securityOptions = securityOptions ?? new AuthSecurityOptions();
        _rateLimiter = rateLimiter ?? new InMemoryAuthRateLimiter();
        _clientAddressProvider = clientAddressProvider ?? new NullAuthClientAddressProvider();
    }

    public async Task<AuthResultDto> AuthenticateAsync(LoginViewModel model, CancellationToken ct = default)
    {
        if (model == null || string.IsNullOrWhiteSpace(model.EmailOrPhone) || string.IsNullOrWhiteSpace(model.Password))
        {
            return AuthResultDto.FailureResult("Vui lòng nhập đầy đủ thông tin đăng nhập.");
        }

        var identifier = model.EmailOrPhone.Trim().ToLowerInvariant();
        var lockoutKey = HashIdentifier("login", identifier);
        var now = DateTimeOffset.UtcNow;

        // Check lockout
        if (_rateLimiter.IsLoginLocked(lockoutKey, now))
        {
            _logger?.LogWarning("Authentication attempt was rejected during the configured lockout period.");
            return AuthResultDto.FailureResult(InvalidLoginMessage);
        }

        var loginWindow = TimeSpan.FromMinutes(Math.Max(1, _securityOptions.LoginRequestWindowMinutes));
        if (!TryAcquireClientAddressLimit(
                "login-ip",
                now,
                _securityOptions.LoginIpRequestLimit,
                loginWindow) ||
            !_rateLimiter.TryAcquire(lockoutKey, now, _securityOptions.LoginRequestLimit, loginWindow))
        {
            _logger?.LogWarning("Authentication attempt was rejected by the configured request limits.");
            return AuthResultDto.FailureResult(InvalidLoginMessage);
        }

        var user = await _userRepository.GetByEmailOrPhoneAsync(identifier, ct);
        var passwordHashToCheck = user?.PasswordHash ?? DummyPasswordHash;
        var passwordValid = _passwordHasher.VerifyPassword(model.Password, passwordHashToCheck);
        if (user == null || !user.IsActive || !passwordValid)
        {
            RecordFailedAttempt(lockoutKey, now);
            return AuthResultDto.FailureResult(InvalidLoginMessage);
        }

        // Reset lockout on success
        _rateLimiter.ClearLoginFailures(lockoutKey);

        var roleName = user.Role?.Name ?? "Customer";

        return AuthResultDto.SuccessResult(
            user.Id,
            user.Email,
            user.FullName,
            user.PhoneNumber,
            roleName);
    }

    public async Task<AuthResultDto> RegisterCustomerAsync(RegisterViewModel model, CancellationToken ct = default)
    {
        if (model == null)
        {
            return AuthResultDto.FailureResult("Dữ liệu đăng ký không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(model.FullName) || model.FullName.Trim().Length < 2)
        {
            return AuthResultDto.FailureResult("Họ và tên phải có ít nhất 2 ký tự.");
        }

        if (string.IsNullOrWhiteSpace(model.Email) || !Regex.IsMatch(model.Email.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            return AuthResultDto.FailureResult("Địa chỉ email không hợp lệ.");
        }

        var normalizedEmail = model.Email.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(model.PhoneNumber) || !Regex.IsMatch(model.PhoneNumber.Trim(), @"^(0|\+84)[3|5|7|8|9][0-9]{8}$"))
        {
            return AuthResultDto.FailureResult("Số điện thoại không hợp lệ (Ví dụ: 0912345678).");
        }

        var normalizedPhone = model.PhoneNumber.Trim();

        if (string.IsNullOrWhiteSpace(model.Password) || model.Password.Length < 6)
        {
            return AuthResultDto.FailureResult("Mật khẩu phải có ít nhất 6 ký tự.");
        }

        if (model.Password != model.ConfirmPassword)
        {
            return AuthResultDto.FailureResult("Mật khẩu xác nhận không khớp.");
        }

        // Check if email already exists
        if (await _userRepository.ExistsByEmailAsync(normalizedEmail, null, ct))
        {
            return AuthResultDto.FailureResult("Địa chỉ email này đã được sử dụng.");
        }

        // Check if phone already exists
        if (await _userRepository.ExistsByPhoneAsync(normalizedPhone, null, ct))
        {
            return AuthResultDto.FailureResult("Số điện thoại này đã được sử dụng.");
        }

        // Get or verify "Customer" role - users can NEVER register as Admin
        var customerRole = await _userRepository.GetRoleByNameAsync("Customer", ct);
        if (customerRole == null)
        {
            var allRoles = await _userRepository.GetAllRolesAsync(ct);
            customerRole = allRoles.Find(r =>
                string.Equals(r.Name, "Customer", StringComparison.OrdinalIgnoreCase));
        }

        if (customerRole == null)
        {
            return AuthResultDto.FailureResult(
                "Không thể đăng ký tài khoản lúc này. Vui lòng liên hệ quản trị viên.");
        }

        var newUser = new User
        {
            FullName = model.FullName.Trim(),
            Email = normalizedEmail,
            PhoneNumber = normalizedPhone,
            PasswordHash = _passwordHasher.HashPassword(model.Password),
            RoleId = customerRole.Id,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var created = await _userRepository.CreateAsync(newUser, ct);

        return AuthResultDto.SuccessResult(
            created.Id,
            created.Email,
            created.FullName,
            created.PhoneNumber,
            "Customer");
    }

    public async Task<UserProfileDto?> GetUserProfileAsync(long userId, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user == null) return null;

        return new UserProfileDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            RoleName = user.Role?.Name ?? "Customer",
            CreatedAt = user.CreatedAt
        };
    }

    public async Task<AuthResultDto> UpdateProfileAsync(long userId, UpdateProfileRequestDto request, CancellationToken ct = default)
    {
        if (request == null) return AuthResultDto.FailureResult("Dữ liệu không hợp lệ.");

        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user == null) return AuthResultDto.FailureResult("Không tìm thấy thông tin tài khoản.");

        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length < 2)
        {
            return AuthResultDto.FailureResult("Họ và tên phải có ít nhất 2 ký tự.");
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber) || !Regex.IsMatch(request.PhoneNumber.Trim(), @"^(0|\+84)[3|5|7|8|9][0-9]{8}$"))
        {
            return AuthResultDto.FailureResult("Số điện thoại không hợp lệ (Ví dụ: 0912345678).");
        }

        var normalizedPhone = request.PhoneNumber.Trim();

        // Check if new phone is used by someone else
        if (await _userRepository.ExistsByPhoneAsync(normalizedPhone, userId, ct))
        {
            return AuthResultDto.FailureResult("Số điện thoại này đã được sử dụng bởi tài khoản khác.");
        }

        user.FullName = request.FullName.Trim();
        user.PhoneNumber = normalizedPhone;

        await _userRepository.UpdateAsync(user, ct);

        return AuthResultDto.SuccessResult(user.Id, user.Email, user.FullName, user.PhoneNumber, user.Role?.Name ?? "Customer");
    }

    public async Task<AuthResultDto> ChangePasswordAsync(long userId, ChangePasswordRequestDto request, CancellationToken ct = default)
    {
        if (request == null) return AuthResultDto.FailureResult("Dữ liệu không hợp lệ.");

        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            return AuthResultDto.FailureResult("Vui lòng nhập mật khẩu hiện tại.");
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
        {
            return AuthResultDto.FailureResult("Mật khẩu mới phải có ít nhất 6 ký tự.");
        }

        if (request.NewPassword != request.ConfirmNewPassword)
        {
            return AuthResultDto.FailureResult("Mật khẩu xác nhận không khớp.");
        }

        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user == null) return AuthResultDto.FailureResult("Không tìm thấy thông tin tài khoản.");

        if (!_passwordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash))
        {
            return AuthResultDto.FailureResult("Mật khẩu hiện tại không chính xác.");
        }

        user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        await _userRepository.UpdateAsync(user, ct);

        return AuthResultDto.SuccessResult(user.Id, user.Email, user.FullName, user.PhoneNumber, user.Role?.Name ?? "Customer");
    }

    public async Task<AuthResultDto> RequestPasswordResetAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return PasswordResetRequestResult();
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var resetKey = HashIdentifier("reset", normalizedEmail);
        if (!TryAcquireResetLimit("reset-account", resetKey, now))
        {
            _logger?.LogWarning("Password reset request was throttled by the configured account limit.");
            return PasswordResetRequestResult();
        }

        if (_passwordResetTokenRepository == null || _emailSender == null)
        {
            return PasswordResetRequestResult();
        }

        var user = await _userRepository.GetByEmailAsync(normalizedEmail, ct);
        if (user != null && user.IsActive)
        {
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var tokenKey = HashResetToken(token);
            var expiresAt = now.AddMinutes(Math.Max(1, _securityOptions.PasswordResetTokenLifetimeMinutes));
            await _passwordResetTokenRepository.IssueAsync(user.Id, tokenKey, now, expiresAt, ct);
            try
            {
                if (!await _emailSender.SendPasswordResetAsync(user.Email, token, ct))
                {
                    await _passwordResetTokenRepository.InvalidateAsync(tokenKey, DateTimeOffset.UtcNow, ct);
                    _logger?.LogWarning("Password reset email delivery was not accepted by the configured sender.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await _passwordResetTokenRepository.InvalidateAsync(tokenKey, DateTimeOffset.UtcNow, ct);
                _logger?.LogError(ex, "Could not send a password reset email.");
            }
        }

        return PasswordResetRequestResult();
    }

    public async Task<AuthResultDto> ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken ct = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Token))
        {
            return AuthResultDto.FailureResult(InvalidResetTokenMessage);
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
        {
            return AuthResultDto.FailureResult("Mật khẩu mới phải có ít nhất 6 ký tự.");
        }

        if (request.NewPassword != request.ConfirmNewPassword)
        {
            return AuthResultDto.FailureResult("Mật khẩu xác nhận không khớp.");
        }

        if (!TryAcquireResetLimit(
                "reset-token",
                HashIdentifier("reset-token", request.Token),
                DateTimeOffset.UtcNow))
        {
            _logger?.LogWarning("Password reset attempt was throttled by the configured token limit.");
            return AuthResultDto.FailureResult(InvalidResetTokenMessage);
        }

        if (_passwordResetTokenRepository == null)
        {
            return AuthResultDto.FailureResult(InvalidResetTokenMessage);
        }

        var tokenKey = HashResetToken(request.Token);
        var user = await _passwordResetTokenRepository.ConsumeAsync(
            tokenKey,
            _passwordHasher.HashPassword(request.NewPassword),
            DateTimeOffset.UtcNow,
            ct);
        if (user == null)
        {
            return AuthResultDto.FailureResult(InvalidResetTokenMessage);
        }

        return AuthResultDto.SuccessResult(user.Id, user.Email, user.FullName, user.PhoneNumber, user.Role?.Name ?? "Customer");
    }

    public async Task<int> ClaimGuestBookingsAsync(long userId, string guestOwnerIdentifier, CancellationToken ct = default)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(guestOwnerIdentifier))
        {
            return 0;
        }

        return await _bookingRepository.ClaimGuestBookingsAsync(userId, guestOwnerIdentifier, ct);
    }

    private void RecordFailedAttempt(string identifierHash, DateTimeOffset now)
    {
        if (!_rateLimiter.TryRecordLoginFailure(
            identifierHash,
            now,
            _securityOptions.LoginFailureLimit,
            TimeSpan.FromMinutes(Math.Max(1, _securityOptions.LoginLockoutMinutes))))
        {
            _logger?.LogError("Authentication failure could not be recorded because the rate limiter is at capacity.");
        }
    }

    private bool TryAcquireResetLimit(string purpose, string key, DateTimeOffset now)
    {
        var window = TimeSpan.FromMinutes(Math.Max(1, _securityOptions.PasswordResetWindowMinutes));
        return TryAcquireClientAddressLimit(
                   "reset-ip",
                   now,
                   _securityOptions.PasswordResetIpRequestLimit,
                   window) &&
               _rateLimiter.TryAcquire(
                   $"{purpose}:{key}",
                   now,
                   _securityOptions.PasswordResetRequestLimit,
                   window);
    }

    private bool TryAcquireClientAddressLimit(
        string purpose,
        DateTimeOffset now,
        int limit,
        TimeSpan window)
    {
        var address = _clientAddressProvider.GetClientAddress();
        if (string.IsNullOrWhiteSpace(address) || !IPAddress.TryParse(address, out var ipAddress))
        {
            return true;
        }

        var normalizedAddress = ipAddress.IsIPv4MappedToIPv6
            ? ipAddress.MapToIPv4().ToString()
            : ipAddress.ToString();
        return _rateLimiter.TryAcquire($"{purpose}:{normalizedAddress}", now, limit, window);
    }

    private static string HashIdentifier(string purpose, string identifier) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{purpose}:{identifier}")));

    private static AuthResultDto PasswordResetRequestResult() => new()
    {
        Success = true,
        ErrorMessage = ResetRequestAcceptedMessage
    };

    private static string HashResetToken(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
