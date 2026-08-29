using System.Data;
using Aqsat.Application.Common;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aqsat.Infrastructure.Jobs;

/// <summary>
/// One-time (idempotent, re-runnable) conversion behind the owner's 2026-08-28 decision to store
/// national IDs as PLAINTEXT (CLAUDE.md rule 12 rewritten). Migration RemoveNationalIdEncryption
/// renamed the old AES-GCM varbinary columns to NationalIdEncrypted and added the new plaintext
/// NationalId columns — SQL cannot decrypt AES-GCM, so the value conversion happens here in the
/// application: batches of 500 per agency per table, each decrypt output normalized to Latin digits
/// before being written (Customer.NationalIdHash recomputed alongside). Once no legacy value
/// remains in a table (across every agency — RLS forces the count to be per-agency), the
/// NationalIdEncrypted column is dropped. Re-running after a clean pass is a no-op.
/// </summary>
public sealed class NationalIdPlaintextBackfillJob(
    AppDbContext dbContext,
    IFieldEncryptor fieldEncryptor,
    ILogger<NationalIdPlaintextBackfillJob> logger)
{
    private const int BatchSize = 500;
    private static readonly string[] Tables = ["Customers", "Marketers"];

    /// <summary>Rows whose legacy value cannot be decrypted (corrupt/foreign blob) — excluded from
    /// subsequent batches so the loop terminates; the column stays in place and the next startup
    /// retries them.</summary>
    private readonly HashSet<Guid> blockedIds = new();

    public async Task RunAsync(CancellationToken ct = default)
    {
        var agencyIds = await dbContext.Organizations.AsNoTracking()
            .Where(o => o.Level == OrganizationLevel.Agency)
            .Select(o => o.Id)
            .ToListAsync(ct);

        foreach (var table in Tables)
        {
            blockedIds.Clear();

            foreach (var agencyId in agencyIds)
            {
                AgencyContext.Current = agencyId;
                await BackfillBatchesAsync(table, ct);
                dbContext.ChangeTracker.Clear();
            }

            var remaining = await CountRemainingAsync(table, agencyIds, ct);
            if (remaining > 0)
            {
                logger.LogWarning(
                    "NationalIdPlaintextBackfillJob: {Table} still has {Count} row(s) with a legacy NationalIdEncrypted value — the column is kept and the next startup retries.",
                    table, remaining);
                continue;
            }

            await DropLegacyColumnAsync(table, ct);
        }
    }

    private async Task BackfillBatchesAsync(string table, CancellationToken ct)
    {
        while (true)
        {
            // BatchSize+1 non-blocked rows are read so a full batch can be told apart from "exactly
            // BatchSize rows remain" (which must terminate, not loop forever on an empty re-read).
            var batch = await ReadLegacyBatchAsync(table, ct);
            if (batch.Count == 0)
            {
                return;
            }

            foreach (var (id, encrypted) in batch.Take(BatchSize))
            {
                string plaintext;
                try
                {
                    plaintext = fieldEncryptor.Decrypt(encrypted);
                }
                catch (Exception ex)
                {
                    blockedIds.Add(id);
                    logger.LogError(
                        ex, "NationalIdPlaintextBackfillJob: cannot decrypt {Table} row {Id} — skipped for this run, retried on next startup.", table, id);
                    continue;
                }

                var normalized = DigitNormalizer.ToLatin(plaintext).Trim();
                if (normalized.Length == 0)
                {
                    blockedIds.Add(id);
                    logger.LogWarning(
                        "NationalIdPlaintextBackfillJob: {Table} row {Id} decrypts to an empty national ID — skipped for this run, retried on next startup.", table, id);
                    continue;
                }

                if (table == "Customers")
                {
                    var customer = await dbContext.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
                    if (customer is null)
                    {
                        continue;
                    }

                    customer.NationalId = normalized;
                    customer.NationalIdHash = fieldEncryptor.Hash(normalized);
                }
                else
                {
                    var marketer = await dbContext.Marketers.FirstOrDefaultAsync(m => m.Id == id, ct);
                    if (marketer is null)
                    {
                        continue;
                    }

                    marketer.NationalId = normalized;
                }

                await dbContext.SaveChangesAsync(ct);
            }

            dbContext.ChangeTracker.Clear();
            if (batch.Count <= BatchSize)
            {
                return;
            }
        }
    }

    /// <summary>Reads through a raw connection (the legacy column is not part of the EF model) —
    /// stamped with the current agency so the RLS policy only ever exposes that agency's rows.</summary>
    private async Task<List<(Guid Id, byte[] Encrypted)>> ReadLegacyBatchAsync(string table, CancellationToken ct)
    {
        var results = new List<(Guid, byte[])>();
        await using var connection = new SqlConnection(dbContext.Database.GetConnectionString());
        await connection.OpenAsync(ct);
        await StampAgencyAsync(connection, ct);

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT Id, NationalIdEncrypted FROM [dbo].[{table}] " +
            "WHERE NationalIdEncrypted IS NOT NULL AND NationalId IS NULL ORDER BY Id";
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct) && results.Count <= BatchSize)
        {
            var id = reader.GetGuid(0);
            if (blockedIds.Contains(id))
            {
                continue;
            }

            results.Add((id, (byte[])reader.GetValue(1)));
        }

        return results;
    }

    /// <summary>RLS hides other agencies' rows from any single session, so the "is anything left?"
    /// check has to sum one stamped count per agency before the legacy column may be dropped.</summary>
    private async Task<int> CountRemainingAsync(string table, IReadOnlyList<Guid> agencyIds, CancellationToken ct)
    {
        var total = 0;
        foreach (var agencyId in agencyIds)
        {
            await using var connection = new SqlConnection(dbContext.Database.GetConnectionString());
            await connection.OpenAsync(ct);
            await StampAgencyAsync(connection, agencyId, ct);

            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM [dbo].[{table}] WHERE NationalIdEncrypted IS NOT NULL";
            total += Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        }

        return total;
    }

    private async Task DropLegacyColumnAsync(string table, CancellationToken ct)
    {
        await using var connection = new SqlConnection(dbContext.Database.GetConnectionString());
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF COL_LENGTH('dbo.{table}', 'NationalIdEncrypted') IS NOT NULL " +
            $"ALTER TABLE [dbo].[{table}] DROP COLUMN NationalIdEncrypted";
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task StampAgencyAsync(SqlConnection connection, CancellationToken ct) =>
        await StampAgencyAsync(connection, (object?)AgencyContext.Current ?? DBNull.Value, ct);

    private static async Task StampAgencyAsync(SqlConnection connection, object agencyId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC sp_set_session_context @key = N'AgencyId', @value = @agencyId;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@agencyId";
        parameter.DbType = DbType.Guid;
        parameter.Value = agencyId;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(ct);
    }
}