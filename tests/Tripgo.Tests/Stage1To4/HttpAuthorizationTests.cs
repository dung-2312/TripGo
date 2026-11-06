using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tripgo.Tests.Stage1To4;

/// <summary>Runs the real application pipeline over HTTP against the isolated database.</summary>
public sealed class TripgoWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", TestDatabase.ConnectionString);
    }
}

[Collection("Database")]
public sealed class HttpAuthorizationTests : IClassFixture<TripgoWebFactory>
{
    private readonly TripgoWebFactory _factory;

    public HttpAuthorizationTests(TripgoWebFactory factory)
    {
        _factory = factory;
    }

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/operators")]
    [InlineData("/admin/routes")]
    [InlineData("/admin/fleet")]
    [InlineData("/admin/trips")]
    [InlineData("/admin/bookings")]
    [InlineData("/admin/monitoring")]
    [InlineData("/staff/verify-ticket")]
    [InlineData("/account/profile")]
    public async Task AnonymousVisitor_CannotOpenProtectedPages_ByDirectUrl(string url)
    {
        var response = await Client().GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location?.ToString());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData("/register")]
    [InlineData("/schedules")]
    [InlineData("/my-tickets")]
    public async Task PublicPages_Open(string url)
    {
        var response = await Client().GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TicketVerificationApi_RejectsAnonymousCallers()
    {
        var response = await Client().PostAsJsonAsync("/api/tickets/verify", new { Token = "tgt_x", Board = true });

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Redirect, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
    }

    [Fact]
    public async Task PaymentCallback_WithoutAValidSignature_IsRejected()
    {
        var content = new StringContent("{\"transactionCode\":\"TXN-FORGED\",\"amount\":1,\"currency\":\"VND\",\"status\":\"succeeded\"}");

        var response = await Client().PostAsync("/api/payments/callback/SANDBOX", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PaymentCallback_WithAValidSignature_ReachesTheGateway_AndUnknownTransactionsAreOnlyRecorded()
    {
        var provider = _factory.Services.GetRequiredService<Tripgo.Infrastructure.Services.SandboxPaymentProvider>();
        var code = $"TXN-HTTP-{Guid.NewGuid():N}";
        var callback = provider.CreateSignedCallback($"evt-{Guid.NewGuid():N}", code, 1000m, "VND", "succeeded");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/callback/SANDBOX")
        {
            Content = new StringContent(callback.Body)
        };
        request.Headers.Add("X-Tripgo-Signature", callback.Signature);

        var response = await Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("UnknownTransaction", await response.Content.ReadAsStringAsync());

        await using var context = Stage.NewContext();
        Assert.Equal(1, await context.PaymentWebhookEvents.CountAsync(e => e.TransactionCode == code));
        await context.PaymentWebhookEvents.Where(e => e.TransactionCode == code).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task HealthEndpoints_Answer_WithoutLeakingDetails()
    {
        var live = await Client().GetAsync("/health/live");
        var ready = await Client().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.DoesNotContain("TripgoDb", await ready.Content.ReadAsStringAsync());
    }
}
