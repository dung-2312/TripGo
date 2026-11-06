using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Tripgo.Tests.Stage1To4;
using Tripgo.Web.Services;

namespace Tripgo.Tests;

public sealed class BackgroundWorkerPolicyTests
{
    [Fact]
    public void WorkerRunsOnlyWhenExplicitlyEnabledForIsolatedDatabaseInDevelopment()
    {
        var enabled = BackgroundWorkerPolicy.IsEnabled(
            Stage.WorkerConfig,
            new TestHostEnvironment(),
            "BookingConfirmationEmail",
            "TripgoDb_IsolatedTest",
            out var reason);

        Assert.True(enabled, reason);
    }

    [Theory]
    [InlineData("Production", "TripgoDb_IsolatedTest", true)]
    [InlineData("Development", "TripgoDb", true)]
    [InlineData("Development", "TripgoDb_IsolatedTest", false)]
    public void WorkerFailsClosedForNonDevelopmentTargetsAndMissingConfiguration(
        string environmentName,
        string database,
        bool configured)
    {
        var configuration = configured
            ? Stage.WorkerConfig
            : new ConfigurationBuilder().AddInMemoryCollection().Build();
        var environment = new TestHostEnvironment { EnvironmentName = environmentName };

        var enabled = BackgroundWorkerPolicy.IsEnabled(
            configuration,
            environment,
            "BookingExpiration",
            database,
            out _);

        Assert.False(enabled);
    }
}
