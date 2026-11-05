using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tripgo.Application.Interfaces.Persistence;
using Tripgo.Infrastructure.Persistence;
using Tripgo.Infrastructure.Persistence.Repositories;

namespace Tripgo.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' was not found.");
        }

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString, sqlServer =>
                sqlServer.EnableRetryOnFailure()));

        services.AddScoped<ITripRepository, TripRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAdminRepository, AdminRepository>();
        services.AddScoped<IPaymentGatewayRepository, PaymentGatewayRepository>();
        services.AddScoped<ITicketRepository, TicketRepository>();
        services.AddScoped<Tripgo.Application.Interfaces.IEmailSender, Services.SmtpEmailSender>();

        // The sandbox provider is off in Production unless someone turns it on deliberately.
        if (!string.Equals(configuration["Payments:Sandbox:Enabled"], "false", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<Services.SandboxPaymentProvider>();
            services.AddSingleton<Tripgo.Application.Interfaces.IPaymentProvider>(
                provider => provider.GetRequiredService<Services.SandboxPaymentProvider>());
        }
        services.AddScoped<Tripgo.Application.Interfaces.ISandboxFixtureService, Tripgo.Infrastructure.Services.SandboxFixtureService>();

        return services;
    }
}