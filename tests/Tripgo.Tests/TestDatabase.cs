using Microsoft.Data.SqlClient;

namespace Tripgo.Tests;

/// <summary>
/// The single source of the integration-test connection string. A developer whose machine has a
/// different local SQL Server sets TRIPGO_TEST_CONNECTION_STRING; whatever the source, the database
/// must be exactly TripgoDb_IsolatedTest or every test using it fails before opening a connection.
/// </summary>
internal static class TestDatabase
{
    private const string TeamDefault =
        @"Server=localhost\MSSQLSERVER01;Database=TripgoDb_IsolatedTest;Trusted_Connection=True;TrustServerCertificate=True;";

    public static string ConnectionString { get; } = Resolve();

    private static string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable("TRIPGO_TEST_CONNECTION_STRING");
        var connectionString = string.IsNullOrWhiteSpace(configured) ? TeamDefault : configured;
        var target = new SqlConnectionStringBuilder(connectionString);
        if (!string.Equals(target.InitialCatalog, "TripgoDb_IsolatedTest", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Integration tests may only target TripgoDb_IsolatedTest; '{target.InitialCatalog}' was rejected.");
        }

        return connectionString;
    }
}
