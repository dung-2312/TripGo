namespace Tripgo.Application.Interfaces;

public sealed record AdminActor(long UserId, string Email, string Role, long? OperatorId)
{
    public bool IsAdmin => string.Equals(Role, RoleNames.Admin, StringComparison.OrdinalIgnoreCase);
}

public static class RoleNames
{
    public const string Admin = "Admin";
    public const string Operator = "Operator";
    public const string Customer = "Customer";
}

/// <summary>
/// Service-level authorization. It re-reads the account from the database on every call, so a role
/// claim in an old cookie cannot outlive a deactivated account or a removed role.
/// </summary>
public interface IAdminAccessGuard
{
    /// <exception cref="UnauthorizedAccessException">The current user does not hold one of the roles.</exception>
    Task<AdminActor> RequireRoleAsync(params string[] roles);
}
