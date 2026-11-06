using Tripgo.Application.Interfaces;

namespace Tripgo.Application.Services;

public sealed class NullAuthClientAddressProvider : IAuthClientAddressProvider
{
    public string? GetClientAddress() => null;
}
