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
        services.AddScoped<ITripService, TripService>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddSingleton<IPasswordHasherService, PasswordHasherService>();
        services.AddScoped<IAuthService, AuthService>();

        return services;
    }
}