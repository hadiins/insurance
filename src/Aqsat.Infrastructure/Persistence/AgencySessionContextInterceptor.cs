using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Aqsat.Infrastructure.Persistence;

/// <summary>
/// Stamps every SQL Server connection with the current agency via sp_set_session_context, which the
/// RLS predicate function (migration AddRowLevelSecurity) reads to filter rows. This is the
/// mechanism only — Task 4 is responsible for making sure AgencyContext.Current reflects the
/// authenticated user before any query runs.
///
/// Stamping on open alone is not enough: a connection held open across an AgencyContext change
/// (an explicit transaction, a held-open unit of work like the portal link flow, or a job looping
/// agencies on one DbContext) would keep serving queries under the PREVIOUS agency's RLS scope —
/// the wrong rows or, worse, no rows where RLS silently filters. So every command re-checks the
/// connection's stamped value and re-stamps when the ambient agency changed. The check is a
/// ConditionalWeakTable lookup; the extra round-trip only happens on an actual change.
/// </summary>
public sealed class AgencySessionContextInterceptor : DbConnectionInterceptor, IDbCommandInterceptor, IDbTransactionInterceptor
{
    private static readonly ConditionalWeakTable<DbConnection, StrongBox<Guid?>> StampedAgency = new();

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        // UNCONDITIONAL: ADO.NET pooling runs sp_reset_connection on every reuse, which silently
        // CLEARS SESSION_CONTEXT — a cached "already stamped" decision here would let the next
        // query run with no agency scope at all, which RLS's block predicates then (correctly)
        // reject. The stamp must be re-issued on every open no matter what.
        Stamp(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await StampAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        if (command.Connection is { } connection)
        {
            StampIfChanged(connection);
        }
        return result;
    }

    public async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (command.Connection is { } connection)
        {
            await StampIfChangedAsync(connection, cancellationToken);
        }
        return result;
    }

    public InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        if (command.Connection is { } connection)
        {
            StampIfChanged(connection);
        }
        return result;
    }

    public async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (command.Connection is { } connection)
        {
            await StampIfChangedAsync(connection, cancellationToken);
        }
        return result;
    }

    public InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        if (command.Connection is { } connection)
        {
            StampIfChanged(connection);
        }
        return result;
    }

    public async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        if (command.Connection is { } connection)
        {
            await StampIfChangedAsync(connection, cancellationToken);
        }
        return result;
    }

    // A re-stamp issued INSIDE a transaction that then rolls back may be reverted by SQL Server
    // together with the transaction — forgetting the connection's cached stamp forces a re-stamp
    // before the next command, so the cache can never claim a scope the session no longer has.
    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        ForgetStamp(transaction.Connection);
    }

    public Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ForgetStamp(transaction.Connection);
        return Task.CompletedTask;
    }

    private static void ForgetStamp(DbConnection? connection)
    {
        if (connection is not null)
        {
            StampedAgency.Remove(connection);
        }
    }

    private static void Stamp(DbConnection connection)
    {
        var current = AgencyContext.Current;
        using var command = CreateCommand(connection, current);
        command.ExecuteNonQuery();
        StampedAgency.GetOrCreateValue(connection).Value = current;
    }

    private static async Task StampAsync(DbConnection connection, CancellationToken ct)
    {
        var current = AgencyContext.Current;
        await using var command = CreateCommand(connection, current);
        await command.ExecuteNonQueryAsync(ct);
        StampedAgency.GetOrCreateValue(connection).Value = current;
    }

    private static void StampIfChanged(DbConnection connection)
    {
        var current = AgencyContext.Current;
        var stamped = StampedAgency.GetOrCreateValue(connection);
        if (stamped.Value == current)
        {
            return;
        }

        using var command = CreateCommand(connection, current);
        command.ExecuteNonQuery();
        stamped.Value = current;
    }

    private static async Task StampIfChangedAsync(DbConnection connection, CancellationToken ct)
    {
        var current = AgencyContext.Current;
        var stamped = StampedAgency.GetOrCreateValue(connection);
        if (stamped.Value == current)
        {
            return;
        }

        await using var command = CreateCommand(connection, current);
        await command.ExecuteNonQueryAsync(ct);
        stamped.Value = current;
    }

    private static DbCommand CreateCommand(DbConnection connection, Guid? agencyId)
    {
        var command = connection.CreateCommand();
        command.CommandText = "EXEC sp_set_session_context @key = N'AgencyId', @value = @agencyId;";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@agencyId";
        parameter.DbType = DbType.Guid;
        parameter.Value = (object?)agencyId ?? DBNull.Value;
        command.Parameters.Add(parameter);

        return command;
    }
}
