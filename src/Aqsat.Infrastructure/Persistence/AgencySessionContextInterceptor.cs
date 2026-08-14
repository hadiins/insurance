using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Aqsat.Infrastructure.Persistence;

/// <summary>
/// Stamps every SQL Server connection with the current agency via sp_set_session_context, which the
/// RLS predicate function (migration AddRowLevelSecurity) reads to filter rows. This is the
/// mechanism only — Task 4 is responsible for making sure AgencyContext.Current reflects the
/// authenticated user before any query runs.
/// </summary>
public sealed class AgencySessionContextInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SetSessionContext(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await SetSessionContextAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private static void SetSessionContext(DbConnection connection)
    {
        using var command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    private static async Task SetSessionContextAsync(DbConnection connection, CancellationToken ct)
    {
        await using var command = CreateCommand(connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static DbCommand CreateCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = "EXEC sp_set_session_context @key = N'AgencyId', @value = @agencyId;";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@agencyId";
        parameter.DbType = DbType.Guid;
        parameter.Value = (object?)AgencyContext.Current ?? DBNull.Value;
        command.Parameters.Add(parameter);

        return command;
    }
}
