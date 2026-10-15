using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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

    // Rate limiting / lockout: 5 failed attempts = 5 minutes lockout
    private static readonly ConcurrentDictionary<string, (int FailedCount, DateTimeOffset LockoutUntil)> _lockoutTracker = new();

    // Password reset tokens: token -> (userId, expiresAt)
    private static readonly ConcurrentDictionary<string, (long UserId, DateTimeOffset ExpiresAt)> _resetTokens = new();

    public AuthService(
        IUserRepository userRepository,
        IBookingRepository bookingRepository,
        IPasswordHasherService passwordHasher)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _bookingRepository = bookingRepository ?? throw new ArgumentNullException(nameof(bookingRepository));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
    }

    public async Task<AuthResultDto> AuthenticateAsync(LoginViewModel model, CancellationToken ct = default)
    {
        if (model == null || string.IsNullOrWhiteSpace(model.EmailOrPhone) || string.IsNullOrWhiteSpace(model.Password))
        {
            return AuthResultDto.FailureResult("Vui lòng nhập đầy đủ thông tin đăng nhập.");
        }

        var identifier = model.EmailOrPhone.Trim().ToLowerInvariant();

        // Check lockout
        if (_lockoutTracker.TryGetValue(identifier, out var lockout) && lockout.LockoutUntil > DateTimeOffset.UtcNow)
        {
            var remaining = Math.Ceiling((lockout.LockoutUntil - DateTimeOffset.UtcNow).TotalMinutes);
            return AuthResultDto.FailureResult($"Tài khoản tạm thời bị khóa do nhiều lần đăng nhập không thành công. Vui lòng thử lại sau {remaining} phút.");
        }

        var user = await _userRepository.GetByEmailOrPhoneAsync(identifier, ct);
        if (user == null)
        {
            RecordFailedAttempt(identifier);
            return AuthResultDto.FailureResult("Email, số điện thoại hoặc mật khẩu không chính xác.");
        }

        if (!user.IsActive)
        {
            return AuthResultDto.FailureResult("Tài khoản của bạn đã bị vô hiệu hóa. Vui lòng liên hệ hỗ trợ.");
        }

        var isPasswordValid = _passwordHasher.VerifyPassword(model.Password, user.PasswordHash);
        if (!isPasswordValid)
        {
            RecordFailedAttempt(identifier);
            return AuthResultDto.FailureResult("Email, số điện thoại hoặc mật khẩu không chính xác.");
        }

        // Reset lockout on success
        _lockoutTracker.TryRemove(identifier, out _);

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
        int roleId;
        if (customerRole != null)
        {
            roleId = customerRole.Id;
        }
        else
        {
            var allRoles = await _userRepository.GetAllRolesAsync(ct);
            var matched = allRoles.Find(r => string.Equals(r.Name, "Customer", StringComparison.OrdinalIgnoreCase));
            roleId = matched?.Id ?? 2; // fallback
        }

        var newUser = new User
        {
            FullName = model.FullName.Trim(),
            Email = normalizedEmail,
            PhoneNumber = normalizedPhone,
            PasswordHash = _passwordHasher.HashPassword(model.Password),
            RoleId = roleId,
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
            return AuthResultDto.FailureResult("Vui lòng nhập địa chỉ email.");
        }

        var user = await _userRepository.GetByEmailAsync(email.Trim(), ct);
        if (user != null)
        {
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _resetTokens[token] = (user.Id, DateTimeOffset.UtcNow.AddMinutes(15));
        }

        // Return generic success message to prevent user enumeration
        return new AuthResultDto
        {
            Success = true,
            ErrorMessage = null
        };
    }

    public async Task<AuthResultDto> ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken ct = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Token))
        {
            return AuthResultDto.FailureResult("Mã đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.");
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
        {
            return AuthResultDto.FailureResult("Mật khẩu mới phải có ít nhất 6 ký tự.");
        }

        if (request.NewPassword != request.ConfirmNewPassword)
        {
            return AuthResultDto.FailureResult("Mật khẩu xác nhận không khớp.");
        }

        if (!_resetTokens.TryRemove(request.Token, out var info) || info.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return AuthResultDto.FailureResult("Mã đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.");
        }

        var user = await _userRepository.GetByIdAsync(info.UserId, ct);
        if (user == null)
        {
            return AuthResultDto.FailureResult("Không tìm thấy thông tin tài khoản.");
        }

        user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        await _userRepository.UpdateAsync(user, ct);

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

    private static void RecordFailedAttempt(string identifier)
    {
        _lockoutTracker.AddOrUpdate(
            identifier,
            _ => (1, DateTimeOffset.MinValue),
            (_, current) =>
            {
                var newCount = current.FailedCount + 1;
                if (newCount >= 5)
                {
                    return (newCount, DateTimeOffset.UtcNow.AddMinutes(5));
                }
                return (newCount, DateTimeOffset.MinValue);
            });
    }
}
