using System.Threading.Tasks;

namespace Tripgo.Application.Interfaces;

public interface IBookingSessionContext
{
    Task<string?> GetOwnerIdentifierAsync();
}
