using Microsoft.Extensions.DependencyInjection;

namespace Tripgo.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services)
    {
        // Register Application services here later

        return services;
    }
}