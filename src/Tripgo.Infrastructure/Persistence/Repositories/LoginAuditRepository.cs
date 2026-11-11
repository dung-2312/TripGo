using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Tripgo.Application.Interfaces.Persistence;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Repositories;

public sealed class LoginAuditRepository : ILoginAuditRepository
{
    private readonly AppDbContext _context;

    public LoginAuditRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AddAsync(LoginAuditLog auditLog, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(auditLog);
        _context.LoginAuditLogs.Add(auditLog);
        await _context.SaveChangesAsync(ct);
    }

    public Task<List<LoginAuditLog>> GetByUserIdAsync(long userId, int take = 50, CancellationToken ct = default)
    {
        var limit = Math.Clamp(take, 1, 200);
        return _context.LoginAuditLogs
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }
}
