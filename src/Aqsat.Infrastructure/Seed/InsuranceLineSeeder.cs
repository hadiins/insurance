using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Seed;

/// <summary>
/// docs/PHASE-1-SPEC.md §2.3 — global reference data (no AgencyId), needed before the first policy
/// can be created (manually via Task 6's issuance form, or via import — both require a valid
/// InsuranceLineId). Idempotent get-or-create by Code, safe to call on every startup rather than
/// baked into a migration via HasData, so a new line can be added later without a schema change.
/// </summary>
public static class InsuranceLineSeeder
{
    public const string ThirdPartyCode = "SALIS";

    public static async Task EnsureSeededAsync(AppDbContext context, CancellationToken ct = default)
    {
        var existingCodes = await context.InsuranceLines.AsNoTracking().Select(l => l.Code).ToListAsync(ct);
        if (existingCodes.Count > 0)
        {
            return;
        }

        var thirdParty = new InsuranceLine { Code = ThirdPartyCode, NameFa = "ثالث", RequiresVehicle = true, SortOrder = 1, IsActive = true };
        var body = new InsuranceLine { Code = "BADANEH", NameFa = "بدنه", RequiresVehicle = true, SortOrder = 2, IsActive = true };
        var fire = new InsuranceLine { Code = "ATASH", NameFa = "آتش‌سوزی", RequiresProperty = true, SortOrder = 3, IsActive = true };
        var liability = new InsuranceLine { Code = "MASOOLIYAT", NameFa = "مسئولیت", SortOrder = 4, IsActive = true };
        var accident = new InsuranceLine { Code = "HAVADES", NameFa = "حوادث", SortOrder = 5, IsActive = true };
        var life = new InsuranceLine { Code = "OMR", NameFa = "عمر", SortOrder = 6, IsActive = true };
        var cargo = new InsuranceLine { Code = "BARBARI", NameFa = "باربری", SortOrder = 7, IsActive = true };
        var health = new InsuranceLine { Code = "DARMAN", NameFa = "درمان", SortOrder = 8, IsActive = true };

        context.InsuranceLines.AddRange(thirdParty, body, fire, liability, accident, life, cargo, health);
        context.InsuranceLines.AddRange(
            new InsuranceLine { Parent = liability, Code = "MASOOLIYAT_KARFARMA", NameFa = "مسئولیت کارفرما", SortOrder = 1, IsActive = true },
            new InsuranceLine { Parent = liability, Code = "MASOOLIYAT_HERFEI", NameFa = "مسئولیت حرفه‌ای", SortOrder = 2, IsActive = true },
            new InsuranceLine { Parent = liability, Code = "MASOOLIYAT_OMOMI", NameFa = "مسئولیت عمومی", SortOrder = 3, IsActive = true });

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // InsuranceLine is global (no AgencyId/RLS), so concurrent test runs/callers can race
            // on this exact check-then-insert — a duplicate-key failure here just means someone
            // else won the race and the data is seeded either way (CLAUDE.md rule 24's spirit).
            foreach (var entry in context.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
            {
                entry.State = EntityState.Detached;
            }
        }
    }
}
