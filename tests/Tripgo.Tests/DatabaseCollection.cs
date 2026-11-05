using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Tripgo.Infrastructure.Persistence;
using Tripgo.Infrastructure.Services;

namespace Tripgo.Tests;

/// <summary>
/// Gives every database-backed test class the same prepared isolated database, whatever order they
/// run in. The preparation is idempotent and adds only clearly fictional sandbox data.
/// </summary>
[CollectionDefinition("Database")]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseBaselineFixture>
{
}

public sealed class DatabaseBaselineFixture : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(TestDatabase.ConnectionString, sql => sql.EnableRetryOnFailure().CommandTimeout(120))
            .Options;
        await using var context = new AppDbContext(options);
        context.EnsureWritesTargetIsolatedTestDatabase();

        var fixtures = new SandboxFixtureService(context, NullLogger<SandboxFixtureService>.Instance);
        await fixtures.EnsureSandboxTripsAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
