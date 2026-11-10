using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace NPlusOneBenchmark.Corpus;

/// <summary>
/// Counts database round trips: the benchmark's ground truth. Every SQL command is one round
/// trip, except that all the commands of one <c>SaveChanges</c> call count as one, because
/// providers batch them (SQLite happens to send a command per row, which would make every bulk
/// save look like a save per element).
/// </summary>
public sealed class SqlCounter : DbCommandInterceptor, ISaveChangesInterceptor
{
    private int _count;
    private int _saveDepth;
    private bool _saveSentCommands;

    public int Count => Volatile.Read(ref _count);

    public void Reset() => Volatile.Write(ref _count, 0);

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        OnCommand();
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        OnCommand();
        return new ValueTask<InterceptionResult<DbDataReader>>(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        OnCommand();
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        OnCommand();
        return new ValueTask<InterceptionResult<int>>(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        OnCommand();
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        OnCommand();
        return new ValueTask<InterceptionResult<object>>(result);
    }

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        OnSaveStarting();
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        OnSaveStarting();
        return new ValueTask<InterceptionResult<int>>(result);
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        OnSaveFinished();
        return result;
    }

    public ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        OnSaveFinished();
        return new ValueTask<int>(result);
    }

    public void SaveChangesFailed(DbContextErrorEventData eventData) => OnSaveFinished();

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        OnSaveFinished();
        return Task.CompletedTask;
    }

    private void OnCommand()
    {
        if (_saveDepth > 0)
            _saveSentCommands = true;
        else
            Interlocked.Increment(ref _count);
    }

    private void OnSaveStarting()
    {
        if (_saveDepth++ == 0)
            _saveSentCommands = false;
    }

    private void OnSaveFinished()
    {
        _saveDepth = Math.Max(0, _saveDepth - 1);
        if (_saveDepth == 0 && _saveSentCommands)
        {
            _saveSentCommands = false;
            Interlocked.Increment(ref _count);
        }
    }
}
