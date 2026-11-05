using Tripgo.Infrastructure.Persistence;

namespace Tripgo.Tests.Stage1To4;

public sealed class DatabaseSafetyTests
{
    private const string Good = @"Server=localhost\MSSQLSERVER01;Database=TripgoDb_IsolatedTest;Trusted_Connection=True;";

    [Fact]
    public void TheIsolatedDatabase_OnTheTeamServer_IsApproved()
    {
        Assert.Null(DatabaseTargetGuard.GetWriteTargetViolation(Good));
    }

    [Theory]
    [InlineData(@"Server=localhost\MSSQLSERVER01;Database=TripgoDb;Trusted_Connection=True;")]
    [InlineData(@"Server=localhost\MSSQLSERVER01;Database=master;Trusted_Connection=True;")]
    [InlineData(@"Server=localhost\MSSQLSERVER01;Database=TripgoDb_Isolated;Trusted_Connection=True;")]
    [InlineData("")]
    [InlineData("not a connection string")]
    public void AnyOtherDatabase_IsRefused(string connectionString)
    {
        Assert.NotNull(DatabaseTargetGuard.GetWriteTargetViolation(connectionString));
    }

    [Fact]
    public void TheMainDatabase_IsRefused_EvenOnAnExplicitlyApprovedServer()
    {
        var previous = Environment.GetEnvironmentVariable(DatabaseTargetGuard.ApprovedServerEnvironmentVariable);
        Environment.SetEnvironmentVariable(DatabaseTargetGuard.ApprovedServerEnvironmentVariable, @"(localdb)\MSSQLLocalDB");
        try
        {
            Assert.NotNull(DatabaseTargetGuard.GetWriteTargetViolation(@"Server=(localdb)\MSSQLLocalDB;Database=TripgoDb;Trusted_Connection=True;"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(DatabaseTargetGuard.ApprovedServerEnvironmentVariable, previous);
        }
    }

    [Theory]
    [InlineData(@"(localdb)\MSSQLLocalDB", @"(localdb)\MSSQLLocalDB", true)]
    [InlineData(@"localhost\SQLEXPRESS", @"localhost\SQLEXPRESS", true)]
    [InlineData(@"prod-db.example.com", @"prod-db.example.com", false)]
    [InlineData(@"10.0.0.5\SQL", @"10.0.0.5\SQL", false)]
    [InlineData(@"(localdb)\MSSQLLocalDB", null, false)]
    [InlineData(@"(localdb)\MSSQLLocalDB", @"localhost\SQLEXPRESS", false)]
    [InlineData(@"localhost\MSSQLSERVER01", null, true)]
    public void AServerOptIn_OnlyEverNamesTheLocalMachine(string server, string? approved, bool expected)
    {
        Assert.Equal(expected, DatabaseTargetGuard.IsApprovedServer(server, approved));
    }

    [Fact]
    public void TestConnection_AlwaysTargetsTheIsolatedDatabase()
    {
        Assert.Contains("TripgoDb_IsolatedTest", TestDatabase.ConnectionString);
    }
}
