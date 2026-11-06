using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tripgo.Application.Interfaces;
using Tripgo.Infrastructure;

namespace Tripgo.Tests.Stage1To4;

public sealed class SandboxPaymentRegistrationTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void AddInfrastructure_RegistersSandboxProviderOnlyWhenExplicitlyEnabled(
        string? enabled,
        bool expected)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] =
                @"Server=localhost\MSSQLSERVER01;Database=TripgoDb_IsolatedTest;Trusted_Connection=True;",
            ["Payments:Sandbox:Enabled"] = enabled
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);

        var registered = services.Any(descriptor => descriptor.ServiceType == typeof(IPaymentProvider));
        Assert.Equal(expected, registered);
    }
}
