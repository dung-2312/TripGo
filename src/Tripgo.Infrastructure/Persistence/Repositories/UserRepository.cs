using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Tripgo.Application.Interfaces.Persistence;
using Tripgo.Domain.Entities;

namespace Tripgo.Infrastructure.Persistence.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<User?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        return await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var normalizedEmail = email.Trim().ToLowerInvariant();

        return await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, ct);
    }

    public async Task<User?> GetByPhoneAsync(string phone, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var trimmedPhone = phone.Trim();

        return await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.PhoneNumber == trimmedPhone, ct);
    }

    public async Task<User?> GetByEmailOrPhoneAsync(string identifier, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return null;
        var trimmed = identifier.Trim();
        var normalized = trimmed.ToLowerInvariant();

        return await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalized || u.PhoneNumber == trimmed, ct);
    }

    public async Task<bool> ExistsByEmailAsync(string email, long? excludeUserId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var query = _context.Users.AsNoTracking().Where(u => u.Email.ToLower() == normalizedEmail);
        if (excludeUserId.HasValue)
        {
            query = query.Where(u => u.Id != excludeUserId.Value);
        }

        return await query.AnyAsync(ct);
    }

    public async Task<bool> ExistsByPhoneAsync(string phone, long? excludeUserId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(phone)) return false;
        var trimmedPhone = phone.Trim();

        var query = _context.Users.AsNoTracking().Where(u => u.PhoneNumber == trimmedPhone);
        if (excludeUserId.HasValue)
        {
            query = query.Where(u => u.Id != excludeUserId.Value);
        }

        return await query.AnyAsync(ct);
    }

    public async Task<User> CreateAsync(User user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        _context.Users.Add(user);
        await _context.SaveChangesAsync(ct);
        if (user.Role == null && user.RoleId > 0)
        {
            user.Role = await _context.Roles.FindAsync(new object[] { user.RoleId }, ct);
        }
        return user;
    }

    public async Task<bool> UpdateAsync(User user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        _context.Users.Update(user);
        var rows = await _context.SaveChangesAsync(ct);
        return rows > 0;
    }

    public async Task<Role?> GetRoleByNameAsync(string roleName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(roleName)) return null;
        var normalized = roleName.Trim().ToLowerInvariant();

        return await _context.Roles
            .FirstOrDefaultAsync(r => r.Name.ToLower() == normalized, ct);
    }

    public async Task<List<Role>> GetAllRolesAsync(CancellationToken ct = default)
    {
        return await _context.Roles.AsNoTracking().ToListAsync(ct);
    }
}
