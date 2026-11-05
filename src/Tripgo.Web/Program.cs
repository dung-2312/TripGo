using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication;
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

// 1. Clean Architecture Modules.
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
        options.LoginPath = "/login";
        options.LogoutPath = "/auth/logout";
        options.AccessDeniedPath = "/login";
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<ISignInExchangeStore, SignInExchangeStore>();
builder.Services.AddScoped<IBookingSessionContext, BookingSessionContext>();
builder.Services.AddHostedService<BookingExpirationWorker>();
builder.Services.AddHostedService<BookingConfirmationEmailWorker>();

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

// =========================================================================================
// RUNTIME DATABASE SAFETY GUARD — FAIL-FAST ENFORCEMENT
// =========================================================================================
var rawConnectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
var connectionStringBuilder = new SqlConnectionStringBuilder(rawConnectionString);

if (app.Environment.IsDevelopment())
{
    // In Development, the application must connect strictly to TripgoDb_IsolatedTest.
    // If it targets TripgoDb or any other database, fail-fast and terminate immediately.
    if (!string.Equals(connectionStringBuilder.InitialCatalog, "TripgoDb_IsolatedTest", StringComparison.OrdinalIgnoreCase))
    {
        app.Logger.LogCritical(
            "FAIL-FAST RUNTIME DATABASE SAFETY GUARD: Development runtime operations must connect EXCLUSIVELY to 'TripgoDb_IsolatedTest'. Target database '{Database}' is strictly prohibited. Process terminating immediately.",
            connectionStringBuilder.InitialCatalog);
        throw new InvalidOperationException(
            $"FAIL-FAST SAFETY GUARD: Development runtime operations must connect EXCLUSIVELY to 'TripgoDb_IsolatedTest'. Target database '{connectionStringBuilder.InitialCatalog}' is strictly prohibited.");
    }

    // Verify actual DbContext connection target
    using (var verifyScope = app.Services.CreateScope())
    {
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actualDbName = db.Database.GetDbConnection().Database;
        if (!string.Equals(actualDbName, "TripgoDb_IsolatedTest", StringComparison.OrdinalIgnoreCase))
        {
            app.Logger.LogCritical(
                "FAIL-FAST RUNTIME DATABASE SAFETY GUARD: DbContext connected to '{ActualDatabase}' instead of 'TripgoDb_IsolatedTest'. Process terminating.",
                actualDbName);
            throw new InvalidOperationException(
                $"FAIL-FAST SAFETY GUARD: DbContext connected to prohibited database '{actualDbName}'. Expected 'TripgoDb_IsolatedTest'.");
        }
    }

    app.Logger.LogInformation(
        "FAIL-FAST SAFETY GUARD: Verified runtime database is strictly 'TripgoDb_IsolatedTest' (Server: {Server}).",
        connectionStringBuilder.DataSource);
}

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

// Ensure repeatable sandbox trips exist for development in isolated test database
if (app.Environment.IsDevelopment())
{
    try
    {
        using var scope = app.Services.CreateScope();
        var sandboxService = scope.ServiceProvider.GetService<ISandboxFixtureService>();
        if (sandboxService != null)
        {
            await sandboxService.EnsureSandboxTripsAsync();
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not run sandbox fixture check on startup.");
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

app.MapGet("/auth/exchange", async (
    string? token,
    ISignInExchangeStore store,
    IAuthService authService,
    HttpContext httpContext) =>
{
    if (string.IsNullOrWhiteSpace(token) || !store.TryRetrieve(token, out var data))
    {
        return Results.Redirect("/login?error=invalid_token");
    }

    if (!string.IsNullOrWhiteSpace(data.GuestOwnerIdentifier))
    {
        await authService.ClaimGuestBookingsAsync(data.UserId, data.GuestOwnerIdentifier);
    }

    var ownerIdentifier = data.GuestOwnerIdentifier;
    if (string.IsNullOrWhiteSpace(ownerIdentifier) || !Guid.TryParseExact(ownerIdentifier, "N", out _))
    {
        ownerIdentifier = Guid.NewGuid().ToString("N");
    }

    var claims = new List<System.Security.Claims.Claim>
    {
        new(System.Security.Claims.ClaimTypes.NameIdentifier, data.UserId.ToString()),
        new(System.Security.Claims.ClaimTypes.Name, data.FullName),
        new(System.Security.Claims.ClaimTypes.Email, data.Email),
        new(System.Security.Claims.ClaimTypes.MobilePhone, data.PhoneNumber ?? string.Empty),
        new(System.Security.Claims.ClaimTypes.Role, data.Role),
        new(BookingSessionContext.OwnerClaimType, ownerIdentifier)
    };

    var identity = new System.Security.Claims.ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new System.Security.Claims.ClaimsPrincipal(identity);

    var authProperties = new Microsoft.AspNetCore.Authentication.AuthenticationProperties
    {
        IsPersistent = data.RememberMe,
        ExpiresUtc = data.RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : null,
        AllowRefresh = true
    };

    await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

    var targetUrl = "/";
    if (!string.IsNullOrWhiteSpace(data.ReturnUrl) &&
        data.ReturnUrl.StartsWith('/') && !data.ReturnUrl.StartsWith("//") && !data.ReturnUrl.StartsWith("/\\"))
    {
        targetUrl = data.ReturnUrl;
    }

    return Results.Redirect(targetUrl);
});

app.MapGet("/auth/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    var newOwner = Guid.NewGuid().ToString("N");
    var guestIdentity = new System.Security.Claims.ClaimsIdentity(
        new[] { new System.Security.Claims.Claim(BookingSessionContext.OwnerClaimType, newOwner) },
        CookieAuthenticationDefaults.AuthenticationScheme);

    await httpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new System.Security.Claims.ClaimsPrincipal(guestIdentity),
        new Microsoft.AspNetCore.Authentication.AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(180),
            AllowRefresh = true
        });

    return Results.Redirect("/login?loggedOut=1");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();