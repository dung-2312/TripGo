using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Tripgo.Application.Interfaces;

namespace Tripgo.Web.Services;

public sealed class BookingSessionContext : IBookingSessionContext
{
    public const string OwnerClaimType = "tripgo_booking_session";
    private readonly AuthenticationStateProvider _authenticationStateProvider;

    public BookingSessionContext(AuthenticationStateProvider authenticationStateProvider)
    {
        _authenticationStateProvider = authenticationStateProvider;
    }

    public async Task<string?> GetOwnerIdentifierAsync()
    {
        var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        var value = state.User.FindFirstValue(OwnerClaimType);
        return Guid.TryParseExact(value, "N", out _) ? value : null;
    }

    public async Task<long?> GetUserIdAsync()
    {
        var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        var value = state.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(value, out var userId) && userId > 0 ? userId : null;
    }
}
