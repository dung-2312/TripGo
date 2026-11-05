using Microsoft.Extensions.DependencyInjection;
using Tripgo.Application.Interfaces;
using Tripgo.Application.Services;
using Tripgo.Application.Services.Temporary;

namespace Tripgo.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<ITemporaryBookingStore, InMemoryTemporaryBookingStore>();
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