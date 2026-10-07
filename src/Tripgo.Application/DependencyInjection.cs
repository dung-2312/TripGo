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

        return services;
    }
}