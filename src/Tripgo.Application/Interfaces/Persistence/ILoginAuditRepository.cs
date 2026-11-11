using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tripgo.Domain.Entities;

namespace Tripgo.Application.Interfaces.Persistence;

public interface ILoginAuditRepository
{
    Task AddAsync(LoginAuditLog auditLog, CancellationToken ct = default);
    Task<List<LoginAuditLog>> GetByUserIdAsync(long userId, int take = 50, CancellationToken ct = default);
}
