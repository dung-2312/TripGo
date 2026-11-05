using System.Threading;
using System.Threading.Tasks;
using Tripgo.Application.DTOs;
using Tripgo.Domain.Entities;

namespace Tripgo.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResultDto> AuthenticateAsync(LoginViewModel model, CancellationToken ct = default);
    Task<AuthResultDto> RegisterCustomerAsync(RegisterViewModel model, CancellationToken ct = default);
    Task<UserProfileDto?> GetUserProfileAsync(long userId, CancellationToken ct = default);
    Task<AuthResultDto> UpdateProfileAsync(long userId, UpdateProfileRequestDto request, CancellationToken ct = default);
    Task<AuthResultDto> ChangePasswordAsync(long userId, ChangePasswordRequestDto request, CancellationToken ct = default);
    Task<AuthResultDto> RequestPasswordResetAsync(string email, CancellationToken ct = default);
    Task<AuthResultDto> ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken ct = default);
    Task<int> ClaimGuestBookingsAsync(long userId, string guestOwnerIdentifier, CancellationToken ct = default);
}
