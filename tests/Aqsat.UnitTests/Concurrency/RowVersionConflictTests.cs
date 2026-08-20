using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Concurrency;

/// <summary>
/// Layer 1 (docs/CONCURRENCY.md §2) — the last line of defence even when presence/locks fail. Two
/// separate DbContext instances load the same row, one saves first, and the second's save (still
/// holding the pre-update RowVersion) must be rejected rather than silently overwrite.
/// </summary>
public class RowVersionConflictTests
{
    [Fact]
    public async Task Saving_a_stale_rowversion_throws_a_concurrency_exception_instead_of_silently_overwriting()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(seedContext);

        await using var ctx1 = TestDbContextFactory.Create();
        await using var ctx2 = TestDbContextFactory.Create();
        AgencyContext.Current = agencyA.AgencyId;

        var policyViaCtx1 = await ctx1.Policies.SingleAsync(p => p.Id == agencyA.PolicyId);
        var policyViaCtx2 = await ctx2.Policies.SingleAsync(p => p.Id == agencyA.PolicyId);

        policyViaCtx1.PolicyNumber = "POL-FIRST-WRITER";
        await ctx1.SaveChangesAsync();

        policyViaCtx2.PolicyNumber = "POL-SECOND-WRITER-STALE";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctx2.SaveChangesAsync());

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = agencyA.AgencyId;
        var persisted = await verify.Policies.AsNoTracking().SingleAsync(p => p.Id == agencyA.PolicyId);
        Assert.Equal("POL-FIRST-WRITER", persisted.PolicyNumber);
    }
}
