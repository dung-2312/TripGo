using Tripgo.Domain.Entities;

namespace Tripgo.Application.Interfaces.Persistence;

public interface IPasswordResetTokenRepository
{
    Task IssueAsync(long userId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt, CancellationToken ct = default);
    Task InvalidateAsync(string tokenHash, DateTimeOffset invalidatedAt, CancellationToken ct = default);
    Task<User?> ConsumeAsync(string tokenHash, string newPasswordHash, DateTimeOffset usedAt, CancellationToken ct = default);
}
