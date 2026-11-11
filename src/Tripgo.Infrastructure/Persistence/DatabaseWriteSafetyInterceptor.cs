using System;
using System.Data.Common;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tripgo.Infrastructure.Persistence;

public sealed class DatabaseWriteSafetyInterceptor : DbCommandInterceptor
{
    private static readonly Regex WriteRegex = new(
        @"\b(INSERT|UPDATE|DELETE|DROP|CREATE|ALTER|TRUNCATE|MERGE)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static void EnsureSafeCommand(DbCommand command)
    {
        if (command == null || string.IsNullOrWhiteSpace(command.CommandText))
        {
            return;
        }

        if (WriteRegex.IsMatch(command.CommandText))
        {
            var databaseName = command.Connection?.Database;
            if (!string.IsNullOrWhiteSpace(databaseName) &&
                !string.Equals(databaseName, "TripgoDb_IsolatedTest", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Writes are permitted only against TripgoDb_IsolatedTest. Target database '{databaseName}' is blocked.");
            }
        }
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        EnsureSafeCommand(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        EnsureSafeCommand(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        EnsureSafeCommand(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        EnsureSafeCommand(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        EnsureSafeCommand(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        EnsureSafeCommand(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }
}

