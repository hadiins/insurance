using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Numbering;

/// <summary>
/// docs/TASK-24-POLICY-NUMBER.md §9 steps 3 — Parsian's InsuranceLineCode/PolicyNumberFormat
/// defaults, shared by the one-time migration backfill job (Aqsat.Infrastructure.Jobs.
/// PolicyNumberBackfillJob) and the issuance form's number-suggestion endpoint, so an agency
/// created after the backfill ran still gets these lazily on first use.
/// </summary>
public static class PolicyNumberDefaultsSeeder
{
    public const string ParsianInsurer = "پارسیان";

    private static readonly (string LineCode, string InsuranceLineCode)[] ParsianLineCodes =
    [
        ("1110", InsuranceLineSeeder.ThirdPartyCode),
        ("1120", "BADANEH"),
        ("2210", "ATASH"),
        ("3310", "MASOOLIYAT_KARFARMA"),
    ];

    public static async Task EnsureAgencyDefaultsAsync(AppDbContext dbContext, Guid agencyId, CancellationToken ct)
    {
        var hasFormat = await dbContext.PolicyNumberFormats.AsNoTracking()
            .AnyAsync(f => f.InsurerName == ParsianInsurer, ct);
        if (!hasFormat)
        {
            dbContext.PolicyNumberFormats.Add(new PolicyNumberFormat
            {
                AgencyId = agencyId,
                InsurerName = ParsianInsurer,
                Pattern = "{line}/{agency}/{year}/{serial}",
                Separator = "/",
                LineCodeLength = 4,
                AgencyCodeLength = 6,
                YearDigits = 3,
                SerialLength = 6,
                IsStrict = false,
                IsActive = true,
            });
        }

        var existingCodes = await dbContext.InsuranceLineCodes.AsNoTracking()
            .Where(c => c.InsurerName == ParsianInsurer)
            .Select(c => c.Code)
            .ToListAsync(ct);

        foreach (var (lineCode, insuranceLineCode) in ParsianLineCodes)
        {
            if (existingCodes.Contains(lineCode))
            {
                continue;
            }

            var insuranceLineId = await dbContext.InsuranceLines.AsNoTracking()
                .Where(l => l.Code == insuranceLineCode)
                .Select(l => l.Id)
                .FirstOrDefaultAsync(ct);
            if (insuranceLineId == Guid.Empty)
            {
                continue;
            }

            dbContext.InsuranceLineCodes.Add(new InsuranceLineCode
            {
                AgencyId = agencyId,
                InsuranceLineId = insuranceLineId,
                InsurerName = ParsianInsurer,
                Code = lineCode,
                IsActive = true,
            });
        }

        await dbContext.SaveChangesAsync(ct);
    }
}
