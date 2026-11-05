using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tripgo.Application.DTOs;
using Tripgo.Application.Interfaces;
using Tripgo.Domain.Entities;
using Tripgo.Infrastructure.Persistence;

namespace Tripgo.Web.Services;

public sealed class BookingConfirmationEmailWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);
    private const int BatchSize = 10;
    private const int MaximumAttempts = 5;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BookingConfirmationEmailWorker> _logger;

    public BookingConfirmationEmailWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<BookingConfirmationEmailWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Booking confirmation email outbox polling failed; the worker will retry after the next interval.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var databaseName = db.Database.GetDbConnection().Database;

        if (!string.Equals(databaseName, "TripgoDb_IsolatedTest", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Booking confirmation email delivery is disabled for database {Database}; only TripgoDb_IsolatedTest is permitted.",
                databaseName);
            return;
        }

        var migrations = (await db.Database.GetAppliedMigrationsAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        if (!migrations.Contains("20261105080107_BookingConfirmationEmailOutbox") ||
            !migrations.Any(migration => migration.EndsWith("_Stage1To4_ProductionFoundation", StringComparison.Ordinal)))
        {
            _logger.LogWarning(
                "Booking confirmation email delivery is disabled because its database migration has not been applied.");
            return;
        }

        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        for (var index = 0; index < BatchSize; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var message = await ClaimNextAsync(db, cancellationToken);
            if (message == null)
            {
                return;
            }

            try
            {
                var confirmation = JsonSerializer.Deserialize<BookingConfirmationEmailDto>(message.Payload)
                    ?? throw new InvalidOperationException("The booking confirmation payload is empty.");
                var sent = await emailSender.SendBookingConfirmationAsync(confirmation, cancellationToken);
                if (!sent)
                {
                    await MarkFailedAsync(
                        db,
                        message,
                        "SMTP delivery is not configured.",
                        cancellationToken);
                    continue;
                }

                // "Sent" records that the SMTP server accepted the message, not that it reached an inbox.
                message.Status = BookingConfirmationEmailOutbox.Sent;
                message.SentAt = DateTimeOffset.UtcNow;
                message.LockedUntil = null;
                message.LastError = null;
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "Outbox message {OutboxId} ({MessageType}) accepted by SMTP on attempt {Attempt}.",
                    message.Id, message.MessageType, message.AttemptCount);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Outbox message {OutboxId} ({MessageType}) failed on attempt {Attempt}.",
                    message.Id, message.MessageType, message.AttemptCount);
                await MarkFailedAsync(
                    db,
                    message,
                    exception.Message,
                    cancellationToken);
            }
        }
    }

    private static async Task<BookingConfirmationEmailOutbox?> ClaimNextAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var message = await db.BookingConfirmationEmailOutbox
                .FromSqlInterpolated($"""
                    SELECT TOP (1) *
                    FROM [BookingConfirmationEmailOutbox] WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE ([Status] = {BookingConfirmationEmailOutbox.Pending} AND [NextAttemptAt] <= {now})
                       OR ([Status] = {BookingConfirmationEmailOutbox.Processing} AND [LockedUntil] <= {now})
                    ORDER BY [Id]
                    """)
                .FirstOrDefaultAsync(cancellationToken);

            if (message == null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            // A worker that died mid-send has already used this attempt. A message that crashed on its
            // last attempt stops here rather than being retried without limit.
            if (message.AttemptCount >= MaximumAttempts)
            {
                message.Status = BookingConfirmationEmailOutbox.Failed;
                message.LockedUntil = null;
                message.LastError = "Worker stopped during the final delivery attempt; the outcome is unknown.";
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            message.Status = BookingConfirmationEmailOutbox.Processing;
            message.LockedUntil = now.AddMinutes(2);
            message.LastAttemptAt = now;
            message.AttemptCount++;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return message;
        });
    }

    private static async Task MarkFailedAsync(
        AppDbContext db,
        BookingConfirmationEmailOutbox message,
        string error,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        message.Status = message.AttemptCount >= MaximumAttempts
            ? BookingConfirmationEmailOutbox.Failed
            : BookingConfirmationEmailOutbox.Pending;
        message.LockedUntil = null;
        message.NextAttemptAt = now.Add(GetRetryDelay(message.AttemptCount));
        message.LastError = error.Length <= 1000 ? error : error[..1000];
        await db.SaveChangesAsync(cancellationToken);
    }

    internal static TimeSpan GetRetryDelay(int attemptCount) => attemptCount switch
    {
        <= 1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(15),
        4 => TimeSpan.FromHours(1),
        _ => TimeSpan.FromHours(3)
    };
}
