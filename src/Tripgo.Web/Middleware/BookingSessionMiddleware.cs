using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Tripgo.Web.Services;

namespace Tripgo.Web.Middleware;

public sealed class BookingSessionMiddleware
{
    private readonly RequestDelegate _next;

    public BookingSessionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.User.HasClaim(claim => claim.Type == BookingSessionContext.OwnerClaimType &&
                                           Guid.TryParseExact(claim.Value, "N", out _)))
        {
            var ownerIdentifier = Guid.NewGuid().ToString("N");
            var identity = new ClaimsIdentity(
                new[] { new Claim(BookingSessionContext.OwnerClaimType, ownerIdentifier) },
                CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(180),
                    AllowRefresh = true
                });

            context.User = principal;
        }

        await _next(context);
    }
}
