using System.Net;
using Microsoft.AspNetCore.Http;
using Tripgo.Application.Interfaces;

namespace Tripgo.Web.Services;

public sealed class HttpContextAuthClientAddressProvider : IAuthClientAddressProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextAuthClientAddressProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? GetClientAddress()
    {
        var address = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress;
        if (address is null)
        {
            return null;
        }

        return address.IsIPv4MappedToIPv6
            ? address.MapToIPv4().ToString()
            : address.ToString();
    }
}
