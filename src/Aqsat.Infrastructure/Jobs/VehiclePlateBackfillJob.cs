using Aqsat.Application.Numbering;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Jobs;

/// <summary>
/// docs/TASK-25-IDENTITY-VEHICLE.md §7, step 6 — parses the legacy Vehicle.Plate string into the
/// structured plate columns, batches of 500, no table-wide lock. Paged by a strictly-increasing Id
/// cursor rather than "WHERE PlateNormalized IS NULL" — a filter on the very column this job
/// writes would re-select (and infinite-loop on) any plate that fails to parse, since
/// PlateNormalized stays null forever for those rows. The Id cursor visits every row exactly once
/// per run regardless of outcome; re-running the job later (the eventual "بازتجزیهٔ شماره‌ها" tool)
/// naturally retries anything still unparsed.
/// </summary>
public sealed class VehiclePlateBackfillJob(AppDbContext dbContext)
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

            var cursor = 0L;
            int fetchedThisBatch;
            do
            {
                var batch = await dbContext.Vehicles
                    .Where(v => v.BizId > cursor && v.PlateNormalized == null && v.Plate != null)
                    .OrderBy(v => v.BizId)
                    .Take(BatchSize)
                    .ToListAsync(ct);

                foreach (var vehicle in batch)
                {
                    var parts = PlateParser.Parse(vehicle.Plate);
                    if (parts.IsParsed)
                    {
                        vehicle.PlateTwoDigit = parts.TwoDigit;
                        vehicle.PlateLetter = parts.Letter;
                        vehicle.PlateThreeDigit = parts.ThreeDigit;
                        vehicle.PlateIranCode = parts.IranCode;
                        vehicle.PlateNormalized = parts.Normalized;
                    }
                }

                if (batch.Count > 0)
                {
                    await dbContext.SaveChangesAsync(ct);
                    cursor = batch[^1].BizId;
                }

                dbContext.ChangeTracker.Clear();
                fetchedThisBatch = batch.Count;
            } while (fetchedThisBatch == BatchSize);
        }
    }
}
