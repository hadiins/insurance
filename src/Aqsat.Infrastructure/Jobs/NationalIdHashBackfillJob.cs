using Aqsat.Application.Common;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Jobs;

/// <summary>
/// One-time (idempotent, re-runnable) backfill for the NationalIdHash algorithm change: hashes
/// stored before the switch were unkeyed SHA-256 — trivially reversible from any DB/backup read.
/// This recomputes every customer's hash with the new HMAC-SHA256 (same secret-derived key the
/// AesFieldEncryptor uses now) directly from the encrypted-at-rest value, in batches of 500 per
/// agency, mirroring PolicyNumberBackfillJob's no-single-transaction pattern. Re-running is a
/// no-op for already-backfilled rows: EF Core's byte[] value comparer treats equal content as
/// unchanged, so nothing is written and no audit row is produced.
/// </summary>
public sealed class NationalIdHashBackfillJob(AppDbContext dbContext, IFieldEncryptor fieldEncryptor)
{
    private const int BatchSize = 500;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var agencyIds = await dbContext.Organizations.AsNoTracking()
            .Where(o => o.Level == OrganizationLevel.Agency)
            .Select(o => o.Id)
            .ToListAsync(ct);

        foreach (var agencyId in agencyIds)
        {
            AgencyContext.Current = agencyId;

            int batchCount;
            do
            {
                // The value converter decrypts on read, so customer.NationalId is plaintext here.
                // Rows whose stored hash already equals Hash(plaintext) are left unmodified by
                // EF's change detection — this loop ends once a whole batch is either backfilled
                // or has no NationalId at all.
                var batch = await dbContext.Customers
                    .Where(c => c.NationalId != null && !c.IsDeleted)
                    .OrderBy(c => c.Id)
                    .Take(BatchSize)
                    .ToListAsync(ct);

                foreach (var customer in batch)
                {
                    customer.NationalIdHash = fieldEncryptor.Hash(customer.NationalId!);
                }

                if (batch.Count > 0)
                {
                    await dbContext.SaveChangesAsync(ct);
                }

                dbContext.ChangeTracker.Clear();
                batchCount = batch.Count;
            } while (batchCount == BatchSize);

            dbContext.ChangeTracker.Clear();
        }
    }
}
