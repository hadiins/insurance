using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Api.Controllers;

/// <summary>
/// One-off operational tooling for the 2026-10-06 production cleanup («full wipe of agency 576210
/// on the MonsterASP server»). TEMPORARY BY DESIGN — this file and InternalOpsWipeEndpointTests
/// are removed from the build immediately after the wipe has run.
///
/// Why it exists: production SQL lives on db64192.databaseasp.net, which resolves to a private
/// 10.x address — reachable only from inside MonsterASP's own network. The app is therefore the
/// only host that can execute the wipe.
///
/// Auth: X-Ops-Secret header must equal Platform:BootstrapSecret — the same deploy-time secret
/// that already guards OwnerBootstrapController.
///
/// Safety rails:
/// - only OrganizationLevel.Agency roots are eligible (a Regional/Headquarters code can never
///   wipe the whole tree by accident);
/// - the caller must echo the agency code in `confirm`;
/// - everything runs in ONE transaction that first turns dbo.AgencyAccessPolicy OFF (the default
///   SESSION_CONTEXT predicate would otherwise make every DELETE match zero rows), NOCHECKs all
///   foreign keys (delete order becomes irrelevant), deletes each AgencyId/OrganizationId-scoped
///   table for the agency subtree, removes users whose ONLY membership was this agency (users who
///   also belong elsewhere — and members with no org at all, like the platform owner — survive),
///   then re-CHECKs constraints and turns the policy back on before commit. DDL is transactional,
///   so a failure rolls the policy back to ON; the catch path re-asserts ON on top of that.
/// Mechanics mirror tests/Aqsat.UnitTests/TestDatabaseCleanup.cs.
/// </summary>
[ApiController]
[Route("api/internal-ops/agency-wipe")]
[AllowAnonymous]
public sealed class InternalOpsController(AppDbContext dbContext, IConfiguration configuration) : ControllerBase
{
    /// <summary>Read-only pre-flight: which orgs does this code match, who belongs to them, and how
    /// many rows would each scoped table lose. RLS is toggled off inside the transaction so the
    /// counts are the truth, not the empty result the default SESSION_CONTEXT predicate returns.</summary>
    [HttpGet]
    public async Task<ActionResult<object>> Report([FromQuery] string? code, CancellationToken ct)
    {
        if (!SecretMatches())
        {
            return UnauthorizedSecret();
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequestProblem("کد نمایندگی الزامی است.");
        }

        var conn = dbContext.Database.GetDbConnection();
        DbTransaction? tx = null;
        try
        {
            await conn.OpenAsync(ct);
            tx = await conn.BeginTransactionAsync(ct);

            await ExecAsync(conn, tx, "ALTER SECURITY POLICY dbo.AgencyAccessPolicy WITH (STATE = OFF);", ct);
            if (await ResolveOrgsAsync(conn, tx, code.Trim(), ct) == 0)
            {
                await ExecAsync(conn, tx, "ALTER SECURITY POLICY dbo.AgencyAccessPolicy WITH (STATE = ON);", ct);
                await tx.CommitAsync(ct);
                return NotFoundProblem(code.Trim());
            }

            await ExecAsync(conn, tx, "CREATE TABLE #counts (Tbl NVARCHAR(258) NOT NULL, N INT NOT NULL);", ct);
            var agencyTables = await AgencyTablesAsync(conn, tx, ct);
            var orgTables = await OrgTablesAsync(conn, tx, ct);
            await ExecAsync(conn, tx, BuildCountBatch(agencyTables, "AgencyId") + BuildCountBatch(orgTables, "OrganizationId"), ct);

            var orgs = await OrgRowsAsync(conn, tx, ct);
            var members = await QueryAsync(conn, tx,
                "SELECT DISTINCT u.Id, u.Mobile, u.FullName FROM Users u INNER JOIN UserOrgRoles m ON m.UserId = u.Id WHERE m.OrganizationId IN (SELECT Id FROM #orgs) ORDER BY u.Mobile;",
                r => new { Id = r.GetGuid(0), Mobile = r.GetString(1), FullName = r.GetString(2) }, ct);
            var counts = await CountsAsync(conn, tx, ct);
            var totalRows = Convert.ToInt32(
                await ScalarAsync(conn, tx, "SELECT ISNULL(SUM(N), 0) FROM #counts;", ct));

            await ExecAsync(conn, tx, "ALTER SECURITY POLICY dbo.AgencyAccessPolicy WITH (STATE = ON);", ct);
            var policyOn = await ScalarAsync(conn, tx,
                "SELECT COUNT(*) FROM sys.security_policies WHERE name = 'AgencyAccessPolicy' AND is_enabled = 1;", ct);
            await tx.CommitAsync(ct);

            return Ok(new
            {
                found = true,
                rlsEnabled = Convert.ToInt32(policyOn) == 1,
                orgs,
                members,
                counts,
                totalRows,
            });
        }
        catch
        {
            await SafeRollbackAsync(tx);
            await EnsurePolicyOnAsync();
            throw;
        }
        finally
        {
            await CleanupTempTablesAsync(conn);
            await SafeCloseAsync(conn);
        }
    }
    /// <summary>The wipe itself — see the type-level summary for the exact order of operations.</summary>
    [HttpPost]
    public async Task<ActionResult<object>> Wipe(AgencyWipeRequest request, CancellationToken ct)
    {
        if (!SecretMatches())
        {
            return UnauthorizedSecret();
        }

        var code = request.Code?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequestProblem("کد نمایندگی الزامی است.");
        }

        if (!string.Equals(code, request.Confirm?.Trim(), StringComparison.Ordinal))
        {
            return BadRequestProblem("کد تأیید با کد نمایندگی یکسان نیست.");
        }

        var conn = dbContext.Database.GetDbConnection();
        DbTransaction? tx = null;
        try
        {
            await conn.OpenAsync(ct);
            tx = await conn.BeginTransactionAsync(ct);

            await ExecAsync(conn, tx, "ALTER SECURITY POLICY dbo.AgencyAccessPolicy WITH (STATE = OFF);", ct);
            await ExecAsync(conn, tx, NoCheckAllSql, ct);
            if (await ResolveOrgsAsync(conn, tx, code, ct) == 0)
            {
                await ExecAsync(conn, tx, "ALTER SECURITY POLICY dbo.AgencyAccessPolicy WITH (STATE = ON);", ct);
                await tx.CommitAsync(ct);
                return NotFoundProblem(code);
            }

            var orgs = await OrgRowsAsync(conn, tx, ct);

            await ExecAsync(conn, tx, "CREATE TABLE #counts (Tbl NVARCHAR(258) NOT NULL, N INT NOT NULL);", ct);
            await ExecAsync(conn, tx, DuSql, ct);

            var agencyTables = await AgencyTablesAsync(conn, tx, ct);
            var orgTables = await OrgTablesAsync(conn, tx, ct);
            var userTables = await UserTablesAsync(conn, tx, ct);

            // Order matters: memberships must be gone before Users, and Users must still exist when
            // SecurityEvents is matched by Mobile.
            await ExecAsync(conn, tx, BuildDeleteBatch(agencyTables, "AgencyId"), ct);
            await ExecAsync(conn, tx, BuildDeleteBatch(orgTables, "OrganizationId"), ct);
            await ExecAsync(conn, tx, BuildUserDeleteBatch(userTables), ct);
            await ExecAsync(conn, tx, TailDeleteSql, ct);

            await ExecAsync(conn, tx, CheckAllSql, ct);
            await ExecAsync(conn, tx, "ALTER SECURITY POLICY dbo.AgencyAccessPolicy WITH (STATE = ON);", ct);
            var policyOn = await ScalarAsync(conn, tx,
                "SELECT COUNT(*) FROM sys.security_policies WHERE name = 'AgencyAccessPolicy' AND is_enabled = 1;", ct);

            var counts = await CountsAsync(conn, tx, ct);
            var totalRows = Convert.ToInt32(
                await ScalarAsync(conn, tx, "SELECT ISNULL(SUM(N), 0) FROM #counts;", ct));
            var usersDeleted = Convert.ToInt32(await ScalarAsync(conn, tx, "SELECT COUNT(*) FROM #du;", ct));

            await tx.CommitAsync(ct);

            return Ok(new
            {
                wiped = true,
                rlsEnabled = Convert.ToInt32(policyOn) == 1,
                orgs,
                usersDeleted,
                counts,
                totalRows,
            });
        }
        catch
        {
            await SafeRollbackAsync(tx);
            await EnsurePolicyOnAsync();
            throw;
        }
        finally
        {
            await CleanupTempTablesAsync(conn);
            await SafeCloseAsync(conn);
        }
    }
    private const string OrgsCreateSql = "CREATE TABLE #orgs (Id UNIQUEIDENTIFIER PRIMARY KEY);";

    private const string OrgsSql = """
        ;WITH orgs AS (
            SELECT o.Id FROM Organizations o
            WHERE o.Level = @agencyLevel AND (o.AgencyCode = @code OR o.Code = @code)
            UNION ALL
            SELECT c.Id FROM Organizations c INNER JOIN orgs p ON c.ParentId = p.Id
        )
        INSERT INTO #orgs (Id) SELECT Id FROM orgs;
        SELECT COUNT(*) FROM #orgs;
        """;

    private const string DuSql = """
        CREATE TABLE #du (UserId UNIQUEIDENTIFIER PRIMARY KEY);
        INSERT INTO #du (UserId)
        SELECT DISTINCT r.UserId
        FROM UserOrgRoles r
        WHERE r.OrganizationId IN (SELECT Id FROM #orgs)
          AND NOT EXISTS (
              SELECT 1 FROM UserOrgRoles o
              WHERE o.UserId = r.UserId AND o.OrganizationId NOT IN (SELECT Id FROM #orgs));
        """;

    private const string NoCheckAllSql = """
        DECLARE @s NVARCHAR(MAX) = N'';
        SELECT @s = @s + N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N' NOCHECK CONSTRAINT ALL;' + NCHAR(10)
        FROM sys.tables;
        EXEC sys.sp_executesql @s;
        """;

    private const string CheckAllSql = """
        DECLARE @s NVARCHAR(MAX) = N'';
        SELECT @s = @s + N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N' CHECK CONSTRAINT ALL;' + NCHAR(10)
        FROM sys.tables;
        EXEC sys.sp_executesql @s;
        """;

    private const string TailDeleteSql = """
        DECLARE @n INT;
        DELETE FROM SecurityEvents
        WHERE Mobile IN (SELECT u.Mobile FROM Users u INNER JOIN #du d ON d.UserId = u.Id);
        SET @n = @@ROWCOUNT;
        INSERT INTO #counts (Tbl, N) VALUES (N'SecurityEvents (matched by mobile)', @n);
        DELETE FROM Users WHERE Id IN (SELECT UserId FROM #du);
        SET @n = @@ROWCOUNT;
        INSERT INTO #counts (Tbl, N) VALUES (N'Users (org-only accounts)', @n);
        DELETE FROM Organizations WHERE Id IN (SELECT Id FROM #orgs);
        SET @n = @@ROWCOUNT;
        INSERT INTO #counts (Tbl, N) VALUES (N'Organizations', @n);
        """;
    private const string AgencyTablesSql =
        "SELECT QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) FROM sys.tables t "
        + "WHERE EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = t.object_id AND c.name = N'AgencyId') ORDER BY t.name;";

    private const string OrgTablesSql =
        "SELECT QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) FROM sys.tables t "
        + "WHERE EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = t.object_id AND c.name = N'OrganizationId') ORDER BY t.name;";

    private const string UserTablesSql =
        "SELECT QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) FROM sys.tables t "
        + "INNER JOIN sys.columns c ON c.object_id = t.object_id AND c.name = N'UserId' "
        + "WHERE TYPE_NAME(c.user_type_id) = N'uniqueidentifier' ORDER BY t.name;";

    private const string OrgRowsSql =
        "SELECT Id, Code, Name, AgencyCode, Level FROM Organizations WHERE Id IN (SELECT Id FROM #orgs) ORDER BY Code;";

    private const string CountsSql = "SELECT Tbl, N FROM #counts WHERE N > 0 ORDER BY Tbl;";

    private static Task<List<string>> AgencyTablesAsync(DbConnection conn, DbTransaction tx, CancellationToken ct) =>
        QueryStringsAsync(conn, tx, AgencyTablesSql, ct);

    private static Task<List<string>> OrgTablesAsync(DbConnection conn, DbTransaction tx, CancellationToken ct) =>
        QueryStringsAsync(conn, tx, OrgTablesSql, ct);

    private static Task<List<string>> UserTablesAsync(DbConnection conn, DbTransaction tx, CancellationToken ct) =>
        QueryStringsAsync(conn, tx, UserTablesSql, ct);

    // Wrapped in object so Report and Wipe share one query helper while the response serializer
    // still sees the anonymous shape's property names.
    private static Task<List<object>> OrgRowsAsync(DbConnection conn, DbTransaction tx, CancellationToken ct) =>
        QueryAsync(conn, tx, OrgRowsSql, r => (object)new
        {
            Id = r.GetGuid(0),
            Code = r.GetString(1),
            Name = r.GetString(2),
            AgencyCode = r.IsDBNull(3) ? null : r.GetString(3),
            Level = r.GetByte(4),
        }, ct);

    private static Task<List<object>> CountsAsync(DbConnection conn, DbTransaction tx, CancellationToken ct) =>
        QueryAsync(conn, tx, CountsSql, r => (object)new { Table = r.GetString(0), Count = r.GetInt32(1) }, ct);
    private static string BuildCountBatch(IReadOnlyList<string> tables, string column)
    {
        var sb = new StringBuilder();
        foreach (var table in tables)
        {
            sb.Append("INSERT INTO #counts (Tbl, N) VALUES (N'")
              .Append(table.Replace("'", "''"))
              .Append("', (SELECT COUNT(*) FROM ")
              .Append(table)
              .Append(" WHERE ").Append(column)
              .AppendLine(" IN (SELECT Id FROM #orgs)));");
        }

        return sb.ToString();
    }

    private static string BuildDeleteBatch(IReadOnlyList<string> tables, string column)
    {
        var sb = new StringBuilder("DECLARE @n INT;\n");
        foreach (var table in tables)
        {
            sb.Append("DELETE FROM ").Append(table)
              .Append(" WHERE ").Append(column)
              .AppendLine(" IN (SELECT Id FROM #orgs);")
              .AppendLine("SET @n = @@ROWCOUNT;")
              .Append("INSERT INTO #counts (Tbl, N) VALUES (N'")
              .Append(table.Replace("'", "''"))
              .AppendLine("', @n);");
        }

        return sb.ToString();
    }

    private static string BuildUserDeleteBatch(IReadOnlyList<string> tables)
    {
        var sb = new StringBuilder("DECLARE @n INT;\n");
        foreach (var table in tables)
        {
            sb.Append("DELETE x FROM ").Append(table).AppendLine(" x")
              .AppendLine("INNER JOIN #du d ON d.UserId = x.UserId;")
              .AppendLine("SET @n = @@ROWCOUNT;")
              .Append("INSERT INTO #counts (Tbl, N) VALUES (N'")
              .Append(table.Replace("'", "''"))
              .AppendLine("' + N' (org-only users)', @n);");
        }

        return sb.ToString();
    }
    // Two commands on purpose: a batch that carries parameters is sent as RPC/sp_executesql, and a
    // temp table CREATED inside sp_executesql's scope is dropped when that scope ends — so the
    // CREATE must be a plain parameter-less batch (it persists for the session) before the
    // parameterized INSERT/SELECT can see #orgs.
    private async Task<int> ResolveOrgsAsync(DbConnection conn, DbTransaction tx, string code, CancellationToken ct)
    {
        await ExecAsync(conn, tx, OrgsCreateSql, ct);
        return Convert.ToInt32(await ScalarAsync(conn, tx, OrgsSql, ct,
            ("@code", code), ("@agencyLevel", (byte)OrganizationLevel.Agency)));
    }

    private bool SecretMatches()
    {
        var configured = configuration["Platform:BootstrapSecret"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return false;
        }

        if (!Request.Headers.TryGetValue("X-Ops-Secret", out var raw))
        {
            return false;
        }

        var provided = raw.ToString();
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(configured));
    }

    private static async Task<int> ExecAsync(
        DbConnection conn, DbTransaction? tx, string sql, CancellationToken ct, params (string Name, object? Value)[] args)
    {
        await using var cmd = conn.CreateCommand();
        if (tx is not null)
        {
            cmd.Transaction = tx;
        }

        cmd.CommandText = sql;
        AddParameters(cmd, args);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<object?> ScalarAsync(
        DbConnection conn, DbTransaction? tx, string sql, CancellationToken ct, params (string Name, object? Value)[] args)
    {
        await using var cmd = conn.CreateCommand();
        if (tx is not null)
        {
            cmd.Transaction = tx;
        }

        cmd.CommandText = sql;
        AddParameters(cmd, args);
        return await cmd.ExecuteScalarAsync(ct);
    }
    private static Task<List<string>> QueryStringsAsync(DbConnection conn, DbTransaction tx, string sql, CancellationToken ct) =>
        QueryAsync(conn, tx, sql, r => r.GetString(0), ct);

    private static async Task<List<T>> QueryAsync<T>(
        DbConnection conn, DbTransaction tx, string sql, Func<DbDataReader, T> map, CancellationToken ct,
        params (string Name, object? Value)[] args)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        AddParameters(cmd, args);
        var results = new List<T>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(map(reader));
        }

        return results;
    }

    private static void AddParameters(DbCommand cmd, (string Name, object? Value)[] args)
    {
        foreach (var (name, value) in args)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }
    }
    private async Task EnsurePolicyOnAsync()
    {
        // Belt and braces on top of the transactional rollback: whatever happened, the security
        // policy must never be left disabled — RLS is the product's only tenant boundary.
        try
        {
            var conn = dbContext.Database.GetDbConnection();
            var closeAfter = conn.State != ConnectionState.Open;
            if (closeAfter)
            {
                await conn.OpenAsync();
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = 'AgencyAccessPolicy' AND is_enabled = 0) "
                + "ALTER SECURITY POLICY dbo.AgencyAccessPolicy WITH (STATE = ON);";
            await cmd.ExecuteNonQueryAsync();
            if (closeAfter)
            {
                await conn.CloseAsync();
            }
        }
        catch
        {
            // Best effort — the rollback already restores the pre-transaction state.
        }
    }

    private static async Task CleanupTempTablesAsync(DbConnection conn)
    {
        if (conn.State != ConnectionState.Open)
        {
            return;
        }

        // EF pools this connection — session-scoped temp tables would otherwise survive into an
        // unrelated request's session.
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "IF OBJECT_ID('tempdb..#orgs') IS NOT NULL DROP TABLE #orgs; "
                + "IF OBJECT_ID('tempdb..#counts') IS NOT NULL DROP TABLE #counts; "
                + "IF OBJECT_ID('tempdb..#du') IS NOT NULL DROP TABLE #du;";
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Session teardown drops them anyway.
        }
    }

    private static async Task SafeRollbackAsync(DbTransaction? tx)
    {
        if (tx is null)
        {
            return;
        }

        try
        {
            await tx.RollbackAsync();
        }
        catch
        {
            // Already rolled back or the connection is dead — the server undoes the rest.
        }
    }

    private static async Task SafeCloseAsync(DbConnection conn)
    {
        try
        {
            if (conn.State != ConnectionState.Closed)
            {
                await conn.CloseAsync();
            }
        }
        catch
        {
            // Never mask the original exception.
        }
    }

    private ActionResult UnauthorizedSecret() => Unauthorized(new ProblemDetails
    {
        Status = StatusCodes.Status401Unauthorized,
        Title = "X-Ops-Secret نادرست است.",
    });

    private ActionResult BadRequestProblem(string message) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = message,
    });

    private ActionResult NotFoundProblem(string code) => NotFound(new ProblemDetails
    {
        Status = StatusCodes.Status404NotFound,
        Title = $"نمایندگی با کد «{code}» پیدا نشد.",
    });
}

/// <summary>Body of POST /api/internal-ops/agency-wipe — `Confirm` must repeat `Code` verbatim.</summary>
public sealed record AgencyWipeRequest(string? Code, string? Confirm);
