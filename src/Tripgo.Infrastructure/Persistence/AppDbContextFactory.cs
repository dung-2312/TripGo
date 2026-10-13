using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Data.SqlClient;

namespace Tripgo.Infrastructure.Persistence;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Development";
        var environmentSuffix = string.Concat(environment
            .ToUpperInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_'));
        var connectionString = Environment.GetEnvironmentVariable(
                $"TRIPGO_EF_CONNECTION_STRING_{environmentSuffix}")
            ?? Environment.GetEnvironmentVariable("TRIPGO_EF_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Set TRIPGO_EF_CONNECTION_STRING_{environmentSuffix} or TRIPGO_EF_CONNECTION_STRING explicitly before running EF Core tooling.");
        }

        var target = new SqlConnectionStringBuilder(connectionString);
        var serverName = target.DataSource.Trim();
        if (!string.Equals(serverName, @"Admin-PC\MSSQLSERVER01", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(serverName, @"localhost\MSSQLSERVER01", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "EF Core tooling is restricted to Admin-PC\\MSSQLSERVER01 (or its localhost alias).");
        }

        if (!string.IsNullOrWhiteSpace(target.AttachDBFilename) || target.UserInstance)
        {
            throw new InvalidOperationException(
                "EF Core tooling does not allow attached-file or user-instance database targets.");
        }

        var isProductionDatabase = string.Equals(
            target.InitialCatalog,
            "TripgoDb",
            StringComparison.OrdinalIgnoreCase);
        var isIsolatedTestDatabase = string.Equals(
            target.InitialCatalog,
            "TripgoDb_IsolatedTest",
            StringComparison.OrdinalIgnoreCase);
        var isRehearsalDatabase = target.InitialCatalog.StartsWith(
            "TripgoDb_Phase3A_Rehearsal_",
            StringComparison.OrdinalIgnoreCase);

        if (!isProductionDatabase &&
            !isIsolatedTestDatabase &&
            !(isRehearsalDatabase &&
              string.Equals(
                  Environment.GetEnvironmentVariable("TRIPGO_ALLOW_REHEARSAL_EF_TARGET"),
                  "true",
                  StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "EF Core tooling is restricted to TripgoDb_IsolatedTest, the explicitly approved TripgoDb target, or an explicitly enabled Phase 3A rehearsal database.");
        }

        if (isProductionDatabase &&
            !string.Equals(
                Environment.GetEnvironmentVariable("TRIPGO_ALLOW_PRODUCTION_EF_TARGET"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "EF Core tooling is blocked from TripgoDb. Set TRIPGO_ALLOW_PRODUCTION_EF_TARGET=true only for an explicitly approved production operation.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }
}