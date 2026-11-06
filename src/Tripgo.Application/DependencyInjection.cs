using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tripgo.Application.Configuration;
using Tripgo.Application.Interfaces;
using Tripgo.Application.Services;
using Tripgo.Application.Services.Temporary;

namespace Tripgo.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<AuthSecurityOptions>();
        services.AddSingleton<ITemporaryBookingStore, InMemoryTemporaryBookingStore>();
        services.AddSingleton<IAuthRateLimiter>(provider =>
            new InMemoryAuthRateLimiter(
                provider.GetRequiredService<AuthSecurityOptions>().MaximumRateLimitEntries));
        services.TryAddSingleton<IAuthClientAddressProvider, NullAuthClientAddressProvider>();
        services.AddScoped<TripService>();
        services.AddScoped<ITripService>(provider => provider.GetRequiredService<TripService>());
        services.AddScoped<IAdminAccessGuard, AdminAccessGuard>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IPaymentGatewayService, PaymentGatewayService>();
        services.AddScoped<ITicketService, TicketService>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddSingleton<IPasswordHasherService, PasswordHasherService>();
        services.AddScoped<IAuthService, AuthService>();

        return services;
    }
}