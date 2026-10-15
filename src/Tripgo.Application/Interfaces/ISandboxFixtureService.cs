using System.Threading;
using System.Threading.Tasks;

namespace Tripgo.Application.Interfaces;

public interface ISandboxFixtureService
{
    Task EnsureSandboxTripsAsync(CancellationToken ct = default);
    Task EnsureRolesAndDefaultUsersAsync(CancellationToken ct = default);
    Task RepairPickupDropoffUnicodeAsync(CancellationToken ct = default);
}
