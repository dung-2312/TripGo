using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;
using Microsoft.Data.SqlClient;
using Tripgo.Application;
using Tripgo.Application.Configuration;
using Tripgo.Application.Interfaces;
using Tripgo.Infrastructure;
using Tripgo.Infrastructure.Persistence;
using Tripgo.Web.Components;
using Tripgo.Web.Middleware;
using Tripgo.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Clean Architecture Modules.
builder.Services.AddHttpClient();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<AuthSecurityOptions>(
    builder.Configuration.GetSection("Auth:Security").Get<AuthSecurityOptions>() ?? new AuthSecurityOptions());
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuthClientAddressProvider, HttpContextAuthClientAddressProvider>();

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
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(180);
        options.SlidingExpiration = true;
        options.LoginPath = "/login";
        options.LogoutPath = "/auth/logout";
        options.AccessDeniedPath = "/login";
    });
builder.Services.AddAuthorization(options =>
{
    // Everyone gets a guest booking-session cookie, so "authenticated" alone would admit anonymous
    // visitors. A real account is identified by its user ID claim.
    options.AddPolicy(AccountPolicy.Name, policy =>
        policy.RequireClaim(System.Security.Claims.ClaimTypes.NameIdentifier));
});
builder.Services.AddSingleton<Tripgo.Application.Interfaces.ITicketTokenProtector, TicketTokenProtector>();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseReadinessHealthCheck>("database", tags: new[] { "ready" });
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("public-api", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});
// Compress static text assets (CSS/JS/SVG). HTML and JSON are deliberately excluded: they can carry
// antiforgery tokens, and compressing secret-bearing responses over HTTPS exposes them to BREACH.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = new[]
    {
        "text/css",
        "text/javascript",
        "application/javascript",
        "image/svg+xml",
        "application/wasm"
    };
});
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

app.Logger.LogWarning(
    "Authentication throttling is process-local (in-memory); limits reset on restart and are not shared across instances. Configure a verified distributed limiter before multi-instance production.");

var trustedProxyAddresses = builder.Configuration.GetSection("Auth:TrustedProxies").Get<string[]>() ?? [];
var trustedProxies = new List<IPAddress>();
foreach (var value in trustedProxyAddresses.Where(value => !string.IsNullOrWhiteSpace(value)))
{
    if (!IPAddress.TryParse(value, out var address))
    {
        throw new InvalidOperationException($"Auth:TrustedProxies contains an invalid IP address: '{value}'.");
    }

    trustedProxies.Add(address);
}

if (trustedProxies.Count == 0)
{
    app.Logger.LogWarning(
        "No trusted authentication proxy is configured. Forwarded client addresses will not be trusted; direct connection addresses are used when available.");
}

if (app.Environment.IsProduction())
{
    // Reported loudly but not fatal: an operator may be deliberately running a staging rehearsal.
    if (string.IsNullOrWhiteSpace(keyRingPath))
    {
        app.Logger.LogWarning("DataProtection:KeyRingPath is not set. Sign-in cookies and ticket codes will not survive a restart on another host.");
    }

    if (app.Services.GetServices<Tripgo.Application.Interfaces.IPaymentProvider>().Any(provider => provider.IsSandbox))
    {
        app.Logger.LogCritical("The sandbox payment provider is enabled in Production. Set Payments:Sandbox:Enabled to false.");
    }
}

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
    var migrationViolation = DatabaseTargetGuard.GetWriteTargetViolation(
        builder.Configuration.GetConnectionString("DefaultConnection"));

    if (!app.Environment.IsDevelopment() || migrationViolation != null)
    {
        app.Logger.LogCritical(
            "Automatic migration and seeding were blocked. They are permitted only in Development against TripgoDb_IsolatedTest on an approved SQL Server instance. Reason: {Reason}",
            migrationViolation ?? "The environment is not Development.");
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
if (trustedProxies.Count > 0)
{
    var forwardedHeaders = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = 1,
        RequireHeaderSymmetry = true
    };
    forwardedHeaders.KnownProxies.Clear();
    forwardedHeaders.KnownNetworks.Clear();
    foreach (var trustedProxy in trustedProxies)
    {
        forwardedHeaders.KnownProxies.Add(trustedProxy);
    }
    app.UseForwardedHeaders(forwardedHeaders);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseResponseCompression();
app.UseStaticFiles();
app.UseAuthentication();
app.UseMiddleware<BookingSessionMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// Signed provider callbacks. The signature, not the caller, is what gets trusted here.
app.MapPost("/api/payments/callback/{provider}", async (
    string provider,
    HttpRequest request,
    Tripgo.Application.Interfaces.IPaymentGatewayService gateway,
    CancellationToken cancellationToken) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync(cancellationToken);
    var outcome = await gateway.HandleCallbackAsync(
        provider,
        new Tripgo.Application.DTOs.Payment.PaymentCallbackRequest(body, request.Headers["X-Tripgo-Signature"].ToString()),
        cancellationToken);
    return outcome.Accepted ? Results.Ok(new { outcome.Outcome }) : Results.BadRequest(new { outcome.Outcome });
}).DisableAntiforgery().RequireRateLimiting("public-api");

app.MapPost("/api/tickets/verify", async (
    TicketVerifyRequest request,
    Tripgo.Application.Interfaces.ITicketService tickets,
    CancellationToken cancellationToken) =>
{
    var result = request.Board
        ? await tickets.BoardAsync(request.Token ?? string.Empty, cancellationToken)
        : await tickets.VerifyAsync(request.Token ?? string.Empty, cancellationToken);
    return Results.Ok(result);
}).RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute { Roles = "Admin,Operator" })
  .DisableAntiforgery()
  .RequireRateLimiting("public-api");

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

internal sealed record TicketVerifyRequest(string? Token, bool Board);

/// <summary>Exposed for the integration tests' WebApplicationFactory.</summary>
public partial class Program;