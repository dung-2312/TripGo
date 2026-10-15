using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tripgo.Domain.Entities;

namespace Tripgo.Application.Interfaces.Persistence;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> GetByPhoneAsync(string phone, CancellationToken ct = default);
    Task<User?> GetByEmailOrPhoneAsync(string identifier, CancellationToken ct = default);
    Task<bool> ExistsByEmailAsync(string email, long? excludeUserId = null, CancellationToken ct = default);
    Task<bool> ExistsByPhoneAsync(string phone, long? excludeUserId = null, CancellationToken ct = default);
    Task<User> CreateAsync(User user, CancellationToken ct = default);
    Task<bool> UpdateAsync(User user, CancellationToken ct = default);
    Task<Role?> GetRoleByNameAsync(string roleName, CancellationToken ct = default);
    Task<List<Role>> GetAllRolesAsync(CancellationToken ct = default);
}
