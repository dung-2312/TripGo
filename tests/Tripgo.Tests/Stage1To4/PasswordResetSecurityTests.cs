using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Tripgo.Application.Configuration;
using Tripgo.Application.DTOs;
using Tripgo.Application.Interfaces;
using Tripgo.Application.Services;
using Tripgo.Domain.Entities;
using Tripgo.Domain.Enums;
using Tripgo.Infrastructure.Persistence;
using Tripgo.Infrastructure.Persistence.Repositories;

namespace Tripgo.Tests.Stage1To4;

[Collection("Database")]
public sealed class PasswordResetSecurityTests : IAsyncLifetime
{
    private long _userId;
    private string? _email;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_userId <= 0)
        {
            return;
        }

        await using var context = Stage.NewContext();
        await context.PasswordResetTokens.Where(token => token.UserId == _userId).ExecuteDeleteAsync();
        await context.Users.Where(user => user.Id == _userId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task ResetToken_IsHashedPersistentAndSingleUseAcrossServiceRestart()
    {
        var (userId, email) = await CreateUserAsync();
        var sender = new CapturingEmailSender();
        await using (var context = Stage.NewContext())
        {
            var result = await CreateService(context, sender).RequestPasswordResetAsync(email);
            Assert.True(result.Success);
        }

        var rawToken = Assert.Single(sender.ResetTokens);
        await using (var context = Stage.NewContext())
        {
            var stored = await context.PasswordResetTokens.AsNoTracking().SingleAsync(token => token.UserId == userId);
            Assert.NotEqual(rawToken, stored.TokenHash);
            Assert.Equal(Hash(rawToken), stored.TokenHash);
            Assert.True(stored.ExpiresAt > DateTimeOffset.UtcNow);
        }

        AuthResultDto reset;
        await using (var restartedContext = Stage.NewContext())
        {
            reset = await CreateService(restartedContext, sender).ResetPasswordAsync(ResetRequest(rawToken, "new-password"));
        }

        Assert.True(reset.Success, reset.ErrorMessage);
        await using (var context = Stage.NewContext())
        {
            var user = await context.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == userId);
            Assert.True(new PasswordHasherService().VerifyPassword("new-password", user.PasswordHash));
            Assert.NotNull(await context.PasswordResetTokens.Where(token => token.UserId == userId).Select(token => token.UsedAt).SingleAsync());
        }

        await using (var replayContext = Stage.NewContext())
        {
            var replay = await CreateService(replayContext, sender).ResetPasswordAsync(ResetRequest(rawToken, "another-password"));
            Assert.False(replay.Success);
            Assert.Equal("Mã đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.", replay.ErrorMessage);
        }
    }

    [Fact]
    public async Task NewResetRequestInvalidatesPriorToken_AndExpiredTokensAreRejected()
    {
        var (userId, email) = await CreateUserAsync();
        var sender = new CapturingEmailSender();
        await using (var context = Stage.NewContext())
        {
            var service = CreateService(context, sender);
            await service.RequestPasswordResetAsync(email);
            await service.RequestPasswordResetAsync(email);
        }

        var oldToken = sender.ResetTokens[0];
        var currentToken = sender.ResetTokens[1];
        await using (var context = Stage.NewContext())
        {
            var oldResult = await CreateService(context, sender).ResetPasswordAsync(ResetRequest(oldToken, "old-token-password"));
            Assert.False(oldResult.Success);
        }

        await using (var context = Stage.NewContext())
        {
            var currentResult = await CreateService(context, sender).ResetPasswordAsync(ResetRequest(currentToken, "current-token-password"));
            Assert.True(currentResult.Success, currentResult.ErrorMessage);
        }

        const string expiredToken = "expired-reset-token";
        await using (var context = Stage.NewContext())
        {
            await new PasswordResetTokenRepository(context).IssueAsync(
                userId,
                Hash(expiredToken),
                DateTimeOffset.UtcNow.AddMinutes(-20),
                DateTimeOffset.UtcNow.AddMinutes(-5));
        }

        await using (var context = Stage.NewContext())
        {
            var expiredResult = await CreateService(context, sender).ResetPasswordAsync(ResetRequest(expiredToken, "expired-password"));
            Assert.False(expiredResult.Success);
        }
    }

    [Fact]
    public async Task ResetToken_IsInvalidatedWhenEmailDeliveryIsRejected()
    {
        var (_, email) = await CreateUserAsync();
        var sender = new CapturingEmailSender { DeliveryAccepted = false };
        await using (var context = Stage.NewContext())
        {
            var request = await CreateService(context, sender).RequestPasswordResetAsync(email);
            Assert.True(request.Success);
        }

        var rawToken = Assert.Single(sender.ResetTokens);
        await using (var context = Stage.NewContext())
        {
            var stored = await context.PasswordResetTokens.AsNoTracking().SingleAsync(token => token.UserId == _userId);
            Assert.NotNull(stored.UsedAt);
            var reset = await CreateService(context, sender).ResetPasswordAsync(ResetRequest(rawToken, "unreachable-password"));
            Assert.False(reset.Success);
        }
    }

    private async Task<(long UserId, string Email)> CreateUserAsync()
    {
        await using var context = Stage.NewContext();
        var roleId = await context.Roles.Where(role => role.Name == RoleNames.Customer).Select(role => role.Id).SingleAsync();
        _email = $"reset-{Guid.NewGuid():N}@tripgo.test";
        var user = new User
        {
            FullName = "Reset Test Customer",
            Email = _email,
            PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}",
            PasswordHash = new PasswordHasherService().HashPassword("old-password"),
            RoleId = roleId,
            IsActive = true
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        _userId = user.Id;
        return (_userId, _email);
    }

    private static AuthService CreateService(AppDbContext context, IEmailSender emailSender) =>
        new(
            new UserRepository(context),
            new BookingRepository(context),
            new PasswordHasherService(),
            emailSender,
            passwordResetTokenRepository: new PasswordResetTokenRepository(context),
            securityOptions: new AuthSecurityOptions());

    private static ResetPasswordRequestDto ResetRequest(string token, string newPassword) => new()
    {
        Token = token,
        NewPassword = newPassword,
        ConfirmNewPassword = newPassword
    };

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<string> ResetTokens { get; } = new();
        public bool DeliveryAccepted { get; set; } = true;

        public Task<bool> SendPasswordResetAsync(string email, string token, CancellationToken cancellationToken = default)
        {
            ResetTokens.Add(token);
            return Task.FromResult(DeliveryAccepted);
        }

        public Task<bool> SendBookingConfirmationAsync(BookingConfirmationEmailDto confirmation, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
