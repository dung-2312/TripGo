using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;
using Microsoft.Data.SqlClient;
using Tripgo.Application;
using Tripgo.Application.Interfaces;
using Tripgo.Infrastructure;
using Tripgo.Infrastructure.Persistence;
using Tripgo.Web.Components;
using Tripgo.Web.Middleware;
using Tripgo.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Clean Architecture Modules
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// 2. Add UI & Razor Components
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = ".Tripgo.BookingSession";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(180);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped<IBookingSessionContext, BookingSessionContext>();
builder.Services.AddHostedService<BookingExpirationWorker>();

var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Tripgo");
if (!string.IsNullOrWhiteSpace(keyRingPath))
{
    var certificateThumbprint = builder.Configuration["DataProtection:CertificateThumbprint"];
    if (string.IsNullOrWhiteSpace(certificateThumbprint))
    {
        throw new InvalidOperationException(
            "DataProtection:CertificateThumbprint is required when a persistent key-ring path is configured.");
    }

    using var certificateStore = new X509Store(StoreName.My, StoreLocation.LocalMachine);
    certificateStore.Open(OpenFlags.ReadOnly);
    var certificates = certificateStore.Certificates.Find(
        X509FindType.FindByThumbprint,
        certificateThumbprint.Replace(" ", string.Empty, StringComparison.Ordinal),
        validOnly: true);
    var certificate = certificates
        .OfType<X509Certificate2>()
        .FirstOrDefault(candidate => candidate.HasPrivateKey);
    if (certificate is null)
    {
        throw new InvalidOperationException(
            "The configured Data Protection certificate was not found, is not currently valid, or has no private key in LocalMachine\\My.");
    }

    builder.Services.AddSingleton(certificate);
    dataProtection
        .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
        .ProtectKeysWithCertificate(certificate);
}

var app = builder.Build();

// Startup migration and seeding are restricted to an explicit local isolated-test target.
if (builder.Configuration.GetValue<bool>("ApplyMigrationsOnStartup", false))
{
    var migrationTarget = new SqlConnectionStringBuilder(
        builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty);
    var allowedMigrationServer =
        string.Equals(migrationTarget.DataSource, @"Admin-PC\MSSQLSERVER01", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(migrationTarget.DataSource, @"localhost\MSSQLSERVER01", StringComparison.OrdinalIgnoreCase);

    if (!app.Environment.IsDevelopment() ||
        !allowedMigrationServer ||
        !string.Equals(migrationTarget.InitialCatalog, "TripgoDb_IsolatedTest", StringComparison.OrdinalIgnoreCase))
    {
        app.Logger.LogCritical(
            "Automatic migration and seeding were blocked. They are permitted only in Development against TripgoDb_IsolatedTest on the approved SQL Server instance.");
    }
    else
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var services = scope.ServiceProvider;
            var context = services.GetRequiredService<AppDbContext>();
            await context.Database.MigrateAsync();
            await DbInitializer.SeedAsync(context);
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Startup migration or isolated-test database initialization failed.");
        }
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAuthentication();
app.UseMiddleware<BookingSessionMiddleware>();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();