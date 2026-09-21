using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ActionLedger.Infrastructure.Tests;

/// <summary>
/// Records the SQL EF Core actually sends, so "persists as inserts only" (AD-10) is asserted
/// against the command text rather than against the change tracker — which would only be
/// restating what the test itself set up.
/// </summary>
internal sealed class RecordingCommandInterceptor : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> _commands = new();

    /// <summary>Every command sent since the last <see cref="Clear"/>.</summary>
    public IReadOnlyList<string> Commands => [.. _commands];

    /// <summary>Drops what has been recorded, so a test can ignore its own setup.</summary>
    public void Clear() => _commands.Clear();

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result) =>
        Record(command, base.ReaderExecuting(command, eventData, result));

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default) =>
        new(Record(command, result));

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result) =>
        Record(command, base.NonQueryExecuting(command, eventData, result));

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default) =>
        new(Record(command, result));

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result) =>
        Record(command, base.ScalarExecuting(command, eventData, result));

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default) =>
        new(Record(command, result));

    private TResult Record<TResult>(DbCommand command, TResult result)
    {
        _commands.Enqueue(command.CommandText);

        return result;
    }
}
