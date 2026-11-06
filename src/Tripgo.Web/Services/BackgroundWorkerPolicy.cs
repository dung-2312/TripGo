using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Tripgo.Web.Services;

public static class BackgroundWorkerPolicy
{
    public static bool IsEnabled(
        IConfiguration configuration,
        IHostEnvironment environment,
        string workerName,
        string actualDatabase,
        out string reason)
    {
        var prefix = $"Workers:{workerName}";
        if (!configuration.GetValue<bool>($"{prefix}:Enabled"))
        {
            reason = "the worker is disabled by configuration";
            return false;
        }

        if (!environment.IsDevelopment())
        {
            reason = $"the worker is approved only in Development, not {environment.EnvironmentName}";
            return false;
        }

        var configuredTarget = configuration[$"{prefix}:TargetDatabase"];
        if (!string.Equals(configuredTarget, "TripgoDb_IsolatedTest", StringComparison.OrdinalIgnoreCase))
        {
            reason = "the configured target must be TripgoDb_IsolatedTest";
            return false;
        }

        if (!string.Equals(actualDatabase, "TripgoDb_IsolatedTest", StringComparison.OrdinalIgnoreCase))
        {
            reason = $"the connected database is {actualDatabase}, not TripgoDb_IsolatedTest";
            return false;
        }

        reason = "enabled for the explicitly configured isolated test database";
        return true;
    }
}
