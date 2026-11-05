using Microsoft.Data.SqlClient;

namespace Tripgo.Infrastructure.Persistence;

/// <summary>
/// Central decision point for "may this process run migrations or seed data against that target?".
/// The database name rule is absolute: only <see cref="IsolatedDatabaseName"/> is ever approved.
/// The server rule defaults to the team's known instance; a developer whose machine has a different
/// local instance must opt in explicitly through <see cref="ApprovedServerEnvironmentVariable"/>,
/// and that opt-in can only name a server on the local machine.
/// </summary>
public static class DatabaseTargetGuard
{
    public const string MainDatabaseName = "TripgoDb";
    public const string IsolatedDatabaseName = "TripgoDb_IsolatedTest";
    public const string ApprovedServerEnvironmentVariable = "TRIPGO_APPROVED_LOCAL_SQL_SERVER";

    private static readonly string[] TeamApprovedServers =
    {
        @"Admin-PC\MSSQLSERVER01",
        @"localhost\MSSQLSERVER01"
    };

    public static bool IsIsolatedDatabase(string? databaseName) =>
        string.Equals(databaseName, IsolatedDatabaseName, StringComparison.OrdinalIgnoreCase);

    public static bool IsApprovedServer(string? dataSource, string? explicitlyApprovedServer)
    {
        var server = dataSource?.Trim();
        if (string.IsNullOrEmpty(server))
        {
            return false;
        }

        if (TeamApprovedServers.Any(approved => string.Equals(approved, server, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var approvedByDeveloper = explicitlyApprovedServer?.Trim();
        return !string.IsNullOrEmpty(approvedByDeveloper) &&
               string.Equals(approvedByDeveloper, server, StringComparison.OrdinalIgnoreCase) &&
               IsLocalServer(server);
    }

    /// <summary>Returns null when the target is approved for writes, otherwise the reason it is not.</summary>
    public static string? GetWriteTargetViolation(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "No connection string was supplied.";
        }

        SqlConnectionStringBuilder target;
        try
        {
            target = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            return "The connection string could not be parsed.";
        }

        if (!IsIsolatedDatabase(target.InitialCatalog))
        {
            return $"Database '{target.InitialCatalog}' is not {IsolatedDatabaseName}.";
        }

        if (!string.IsNullOrWhiteSpace(target.AttachDBFilename) || target.UserInstance)
        {
            return "Attached-file and user-instance targets are not allowed.";
        }

        var explicitlyApproved = Environment.GetEnvironmentVariable(ApprovedServerEnvironmentVariable);
        if (!IsApprovedServer(target.DataSource, explicitlyApproved))
        {
            return $"Server '{target.DataSource}' is not approved. Set {ApprovedServerEnvironmentVariable} to name a local server explicitly.";
        }

        return null;
    }

    private static bool IsLocalServer(string server)
    {
        var host = server.Split('\\', 2)[0].Split(',', 2)[0].Trim();
        return host.Equals("(localdb)", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               host.Equals(".", StringComparison.Ordinal) ||
               host.Equals("127.0.0.1", StringComparison.Ordinal) ||
               host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }
}
