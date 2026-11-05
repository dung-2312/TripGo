using Tripgo.Application.Interfaces;
using Tripgo.Application.Interfaces.Persistence;

namespace Tripgo.Application.Services;

public sealed class AdminAccessGuard : IAdminAccessGuard
{
    private readonly IBookingSessionContext _session;
    private readonly IAdminRepository _repository;

    public AdminAccessGuard(IBookingSessionContext session, IAdminRepository repository)
    {
        _session = session;
        _repository = repository;
    }

    public async Task<AdminActor> RequireRoleAsync(params string[] roles)
    {
        var userId = await _session.GetUserIdAsync();
        if (!userId.HasValue)
        {
            throw new UnauthorizedAccessException("Bạn cần đăng nhập để thực hiện thao tác này.");
        }

        var actor = await _repository.GetActiveActorAsync(userId.Value);
        if (actor == null || !roles.Contains(actor.Role, StringComparer.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Bạn không có quyền thực hiện thao tác này.");
        }

        return actor;
    }
}
