using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Tripgo.Application.Interfaces.Persistence;
using Tripgo.Infrastructure.Persistence;

namespace Tripgo.Web.Services;

public sealed class BookingExpirationWorker : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(30);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BookingExpirationWorker> _logger;

    public BookingExpirationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<BookingExpirationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(CleanupInterval);
            do
            {
                if (!await RunCleanupAsync(stoppingToken))
                {
                    return;
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task<bool> RunCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dbName = dbContext.Database.GetDbConnection().Database;
            if (!string.Equals(dbName, "TripgoDb_IsolatedTest", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Booking expiration is disabled for database {Database}; only TripgoDb_IsolatedTest is permitted.",
                    dbName);
                return false;
            }

            var appliedMigrations = (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);
            var requiredMigrations = new[]
            {
                "20261008183111_Phase3A_PersistentBookingCore",
                "20261008191552_Phase3A_BookingSafetyHardening",
                "20261008192859_Phase3A_HoldOwnershipReleaseGate",
                "20261014142606_Phase3A_DirectBookingOwnership",
                "20261015092939_Phase3B_PaymentTransactions"
            };
            var missingMigrations = requiredMigrations
                .Where(migration => !appliedMigrations.Contains(migration))
                .ToArray();

            if (missingMigrations.Length > 0)
            {
                _logger.LogCritical(
                    "Booking expiration is disabled because the database is missing required migrations: {MigrationIds}",
                    string.Join(", ", missingMigrations));
                return false;
            }

            var repository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
            await repository.ExpireStaleHoldsAsync(cancellationToken);
            await repository.ExpireStaleBookingsAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (SqlException exception) when (exception.IsTransient)
        {
            _logger.LogWarning(exception, "Booking expiration cleanup hit a transient SQL error and will retry on the next interval.");
            return true;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _logger.LogInformation(exception, "Another application instance completed booking expiration cleanup first.");
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(exception, "Booking expiration encountered an unhandled error during cleanup; will retry on the next interval.");
            return true;
        }
    }
}
