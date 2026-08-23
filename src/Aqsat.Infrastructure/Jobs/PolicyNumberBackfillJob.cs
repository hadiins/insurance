using Aqsat.Application.Numbering;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Numbering;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Jobs;

/// <summary>
/// docs/TASK-24-POLICY-NUMBER.md §9, steps 3-5 — one-time (re-runnable) backfill for the migration
/// that added the Pn* columns: seeds each agency's Parsian InsuranceLineCode/PolicyNumberFormat
/// defaults if missing, parses every unparsed PolicyNumber in batches of 500 (no single
/// table-wide transaction, per §9's "بدون قفل کردن جدول"), then derives Organization.AgencyCode
/// from the parsed results if it isn't set yet. Safe to re-run: already-parsed rows are skipped,
/// and re-parsing a previously-failed row is exactly the "بازتجزیهٔ شماره‌ها" tool §9 asks for.
/// </summary>
public sealed class PolicyNumberBackfillJob(AppDbContext dbContext)
{
    private const int BatchSize = 500;
    private const string ParsianInsurer = PolicyNumberDefaultsSeeder.ParsianInsurer;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var agencyIds = await dbContext.Organizations.AsNoTracking()
            .Where(o => o.Level == OrganizationLevel.Agency)
            .Select(o => o.Id)
            .ToListAsync(ct);

        foreach (var agencyId in agencyIds)
        {
            AgencyContext.Current = agencyId;

            await PolicyNumberDefaultsSeeder.EnsureAgencyDefaultsAsync(dbContext, agencyId, ct);
            var format = await dbContext.PolicyNumberFormats.AsNoTracking()
                .FirstAsync(f => f.InsurerName == ParsianInsurer, ct);

            int parsedThisAgency;
            do
            {
                // Every attempt — success or failure — leaves either PnIsParsed=true or a non-null
                // PnParseNote, so this filter picks up only rows that have genuinely never been
                // tried. Without the note check, a row that fails every time (a truly unparseable
                // number) would keep matching "!PnIsParsed" forever and this loop would never end.
                var batch = await dbContext.Policies
                    .Where(p => !p.PnIsParsed && p.PnParseNote == null)
                    .OrderBy(p => p.Id)
                    .Take(BatchSize)
                    .ToListAsync(ct);

                foreach (var policy in batch)
                {
                    var parts = PolicyNumberParser.Parse(policy.PolicyNumber, format);
                    policy.PnLineCode = parts.LineCode;
                    policy.PnAgencyCode = parts.AgencyCode;
                    policy.PnYear = parts.Year;
                    policy.PnSerial = parts.Serial;
                    policy.PnIsParsed = parts.IsParsed;
                    policy.PnParseNote = parts.Note;
                }

                if (batch.Count > 0)
                {
                    await dbContext.SaveChangesAsync(ct);
                }

                dbContext.ChangeTracker.Clear();
                parsedThisAgency = batch.Count;
            } while (parsedThisAgency == BatchSize);

            await BackfillAgencyCodeAsync(agencyId, ct);
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <summary>docs/TASK-24-POLICY-NUMBER.md §9 step 4 — "از خود شماره‌های موجود استخراج شود":
    /// the most common PnAgencyCode among this agency's successfully-parsed policies. Never
    /// overwrites a value someone already set.</summary>
    private async Task BackfillAgencyCodeAsync(Guid agencyId, CancellationToken ct)
    {
        var organization = await dbContext.Organizations.FirstAsync(o => o.Id == agencyId, ct);
        if (organization.AgencyCode is not null)
        {
            return;
        }

        var mostCommon = await dbContext.Policies.AsNoTracking()
            .Where(p => p.PnIsParsed && p.PnAgencyCode != null)
            .GroupBy(p => p.PnAgencyCode)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefaultAsync(ct);

        if (mostCommon is not null)
        {
            organization.AgencyCode = mostCommon;
            await dbContext.SaveChangesAsync(ct);
        }
    }
}
