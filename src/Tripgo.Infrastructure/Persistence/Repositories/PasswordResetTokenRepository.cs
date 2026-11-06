using System.Data;
using Microsoft.EntityFrameworkCore;
using Tripgo.Application.Interfaces.Persistence;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Repositories;

public sealed class PasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private readonly AppDbContext _context;

    public PasswordResetTokenRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task IssueAsync(
        long userId,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        CancellationToken ct = default)
    {
        _context.EnsureWritesTargetIsolatedTestDatabase();
        _context.ChangeTracker.Clear();
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var oldTokens = await _context.PasswordResetTokens
                .Where(token => token.UserId == userId || token.ExpiresAt <= createdAt)
                .ToListAsync(ct);
            _context.PasswordResetTokens.RemoveRange(oldTokens);
            _context.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = userId,
                TokenHash = tokenHash,
                CreatedAt = createdAt,
                ExpiresAt = expiresAt
            });
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });
        _context.ChangeTracker.Clear();
    }

    public async Task InvalidateAsync(string tokenHash, DateTimeOffset invalidatedAt, CancellationToken ct = default)
    {
        _context.EnsureWritesTargetIsolatedTestDatabase();
        _context.ChangeTracker.Clear();
        var token = await _context.PasswordResetTokens
            .FirstOrDefaultAsync(candidate => candidate.TokenHash == tokenHash && candidate.UsedAt == null, ct);
        if (token != null)
        {
            token.UsedAt = invalidatedAt;
            await _context.SaveChangesAsync(ct);
        }
        _context.ChangeTracker.Clear();
    }

    public async Task<User?> ConsumeAsync(
        string tokenHash,
        string newPasswordHash,
        DateTimeOffset usedAt,
        CancellationToken ct = default)
    {
        _context.EnsureWritesTargetIsolatedTestDatabase();
        _context.ChangeTracker.Clear();
        var strategy = _context.Database.CreateExecutionStrategy();
        User? consumedUser = null;
        await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var token = await _context.PasswordResetTokens
                .Include(candidate => candidate.User)
                .ThenInclude(user => user.Role)
                .FirstOrDefaultAsync(candidate => candidate.TokenHash == tokenHash, ct);

            if (token == null || token.UsedAt != null || token.ExpiresAt <= usedAt || !token.User.IsActive)
            {
                await transaction.RollbackAsync(ct);
                return;
            }

            token.UsedAt = usedAt;
            token.User.PasswordHash = newPasswordHash;
            var otherTokens = await _context.PasswordResetTokens
                .Where(candidate => candidate.UserId == token.UserId &&
                                    candidate.Id != token.Id &&
                                    candidate.UsedAt == null)
                .ToListAsync(ct);
            foreach (var otherToken in otherTokens)
            {
                otherToken.UsedAt = usedAt;
            }

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            consumedUser = token.User;
        });
        _context.ChangeTracker.Clear();
        return consumedUser;
    }
}
