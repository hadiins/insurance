using Aqsat.Domain;
using Aqsat.Infrastructure.Jobs;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Jobs;

/// <summary>docs/TASK-24-POLICY-NUMBER.md §9 steps 3-5 — the backfill run itself, including the
/// specific bug this review caught: a policy number that never parses must not spin the batch loop
/// forever.</summary>
public class PolicyNumberBackfillJobTests
{
    [Fact]
    public async Task Backfill_parses_well_formed_numbers_leaves_malformed_ones_flagged_and_terminates()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);

        // SeedTwoAgenciesAsync's own policy ("POL-2491-0001") is a real-world malformed case —
        // exactly the kind of row that previously caused an infinite loop.
        AgencyContext.Current = agencyA.AgencyId;
        var wellFormed = new Policy
        {
            AgencyId = agencyA.AgencyId,
            PolicyNumber = "1110/999888/405/000042",
            InsuranceLineId = await context.InsuranceLines.Select(l => l.Id).FirstAsync(),
            CustomerId = agencyA.CustomerId,
            ContractName = "قرارداد آزمایشی",
            IsInstallment = false,
            IssueDate = new DateOnly(2026, 8, 1),
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2027, 8, 1),
            NetPremium = 1,
            ServiceFee = 0,
            DownPayment = 0,
            InstallmentCount = 1,
        };
        context.Policies.Add(wellFormed);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var job = new PolicyNumberBackfillJob(context);

        // WaitAsync throws TimeoutException if this doesn't terminate — the whole point of this
        // test given the infinite-loop bug it caught during review. 90s gives enough margin for
        // the full suite's parallel DB contention (the job itself finishes in ~1s standalone).
        await job.RunAsync().WaitAsync(TimeSpan.FromSeconds(90));

        AgencyContext.Current = agencyA.AgencyId;
        var reloadedWellFormed = await context.Policies.AsNoTracking().FirstAsync(p => p.Id == wellFormed.Id);
        Assert.True(reloadedWellFormed.PnIsParsed);
        Assert.Equal("1110", reloadedWellFormed.PnLineCode);
        Assert.Equal("999888", reloadedWellFormed.PnAgencyCode);
        Assert.Equal("000042", reloadedWellFormed.PnSerial);

        var reloadedMalformed = await context.Policies.AsNoTracking().FirstAsync(p => p.PolicyNumber == "POL-2491-0001");
        Assert.False(reloadedMalformed.PnIsParsed);
        Assert.NotNull(reloadedMalformed.PnParseNote);

        var organization = await context.Organizations.AsNoTracking().FirstAsync(o => o.Id == agencyA.AgencyId);
        Assert.Equal("999888", organization.AgencyCode);

        var seededFormat = await context.PolicyNumberFormats.AsNoTracking().FirstOrDefaultAsync(f => f.InsurerName == "پارسیان");
        Assert.NotNull(seededFormat);

        var seededCodes = await context.InsuranceLineCodes.AsNoTracking().Where(c => c.InsurerName == "پارسیان").ToListAsync();
        Assert.Equal(4, seededCodes.Count);
    }
}
