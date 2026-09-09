using System.Data;
using System.Data.Common;
using Aqsat.Infrastructure.Monitoring;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Aqsat.Infrastructure.Persistence;

/// <summary>
/// Records every EF command's duration into the in-memory slow-query stats. Hooks the *Executed*
/// overloads (not Executing) because only CommandExecutedEventData carries EF's own measured
/// Duration — no Stopwatch on our side. Read-only observation: never throws, never mutates the
/// command, and deliberately silent on command *Failed* (the error path already logs loudly).
/// Registered alongside AgencySessionContextInterceptor in AddInfrastructure.
/// </summary>
public sealed class EfCommandDurationInterceptor(EfCommandDurationStats stats) : DbCommandInterceptor
{
    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        stats.Record(command.CommandText, eventData.Duration);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken ct = default)
    {
        stats.Record(command.CommandText, eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(
        DbCommand command, CommandExecutedEventData eventData, int result)
    {
        stats.Record(command.CommandText, eventData.Duration);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken ct = default)
    {
        stats.Record(command.CommandText, eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(
        DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        stats.Record(command.CommandText, eventData.Duration);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken ct = default)
    {
        stats.Record(command.CommandText, eventData.Duration);
        return ValueTask.FromResult(result);
    }
}
