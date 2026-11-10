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
        services.AddScoped<ILoginAuditRepository, LoginAuditRepository>();
        services.AddScoped<IAdminRepository, AdminRepository>();
        services.AddScoped<IPaymentGatewayRepository, PaymentGatewayRepository>();
        services.AddScoped<Tripgo.Application.Interfaces.Persistence.IPasswordResetTokenRepository, Persistence.Repositories.PasswordResetTokenRepository>();
        services.AddScoped<ITicketRepository, TicketRepository>();
        services.AddScoped<Tripgo.Application.Interfaces.IEmailSender, Services.SmtpEmailSender>();

        // A missing setting must never enable a simulated payment provider, especially in Production.
        if (bool.TryParse(configuration["Payments:Sandbox:Enabled"], out var sandboxEnabled) && sandboxEnabled)
        {
            services.AddSingleton<Services.SandboxPaymentProvider>();
            services.AddSingleton<Tripgo.Application.Interfaces.IPaymentProvider>(
                provider => provider.GetRequiredService<Services.SandboxPaymentProvider>());
        }

        services.AddTransient<Services.VnPayPaymentProvider>();
        if (bool.TryParse(configuration["Payments:VnPay:Enabled"], out var vnpayEnabled) && vnpayEnabled)
        {
            services.AddSingleton<Tripgo.Application.Interfaces.IPaymentProvider>(
                provider => provider.GetRequiredService<Services.VnPayPaymentProvider>());
        }

        services.AddTransient<Services.MomoPaymentProvider>();
        if (bool.TryParse(configuration["Payments:Momo:Enabled"], out var momoEnabled) && momoEnabled)
        {
            services.AddSingleton<Tripgo.Application.Interfaces.IPaymentProvider>(
                provider => provider.GetRequiredService<Services.MomoPaymentProvider>());
        }
        services.AddScoped<Tripgo.Application.Interfaces.ISandboxFixtureService, Tripgo.Infrastructure.Services.SandboxFixtureService>();

        return services;
    }
}