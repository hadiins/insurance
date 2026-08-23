using Aqsat.Domain;
using Aqsat.Infrastructure.Jobs;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Jobs;

/// <summary>docs/TASK-25-IDENTITY-VEHICLE.md §7 step 6.</summary>
public class VehiclePlateBackfillJobTests
{
    [Fact]
    public async Task Backfill_parses_well_formed_plates_leaves_malformed_ones_alone_and_terminates()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);

        // SeedTwoAgenciesAsync's own vehicle ("11الف111") has no Iran-code segment — a real-world
        // unparseable case, and previously the kind of row that would loop forever.
        AgencyContext.Current = agencyA.AgencyId;
        var wellFormed = new Vehicle { AgencyId = agencyA.AgencyId, Plate = "55 الف 555 ایران 55" };
        context.Vehicles.Add(wellFormed);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var job = new VehiclePlateBackfillJob(context);

        await job.RunAsync().WaitAsync(TimeSpan.FromSeconds(90));

        AgencyContext.Current = agencyA.AgencyId;
        var reloadedWellFormed = await context.Vehicles.AsNoTracking().FirstAsync(v => v.Id == wellFormed.Id);
        Assert.Equal("55الف555-55", reloadedWellFormed.PlateNormalized);
        Assert.Equal("55", reloadedWellFormed.PlateTwoDigit);
        Assert.Equal("الف", reloadedWellFormed.PlateLetter);

        var reloadedMalformed = await context.Vehicles.AsNoTracking().FirstAsync(v => v.Plate == "11الف111");
        Assert.Null(reloadedMalformed.PlateNormalized);
    }
}
