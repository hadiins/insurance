using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.DataModel;

/// <summary>
/// Runs against a real local SQL Server database (Task 3's own migrations applied to
/// `(localdb)\MSSQLLocalDB`) — proving RLS actually isolates tenants, not just reviewing the
/// generated SQL by eye. Requires `dotnet ef database update` to have been run first.
/// </summary>
public class RowLevelSecurityTests
{
    [Fact]
    public async Task Unfiltered_query_returns_only_the_current_agencys_rows()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, agencyB) = await DevSeeder.SeedTwoAgenciesAsync(context);

        AgencyContext.Current = agencyA.AgencyId;

        // Deliberately unfiltered DbSet query — RLS must do the filtering, not this LINQ query.
        var visibleCustomers = await context.Customers.AsNoTracking().ToListAsync();

        Assert.Single(visibleCustomers);
        Assert.Equal(agencyA.CustomerId, visibleCustomers[0].Id);
        Assert.DoesNotContain(visibleCustomers, c => c.Id == agencyB.CustomerId);
    }

    [Fact]
    public async Task A_held_open_connection_re_scopes_when_the_ambient_agency_changes()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, agencyB) = await DevSeeder.SeedTwoAgenciesAsync(context);

        using (AgencyContext.BeginScope(agencyA.AgencyId))
        {
            await context.Database.OpenConnectionAsync();
            var asAgencyA = await context.Customers.AsNoTracking().ToListAsync();
            Assert.Equal(agencyA.CustomerId, Assert.Single(asAgencyA).Id);
        }

        // The connection is STILL OPEN, but the ambient agency is now B — exactly the shape of a
        // job looping agencies on one DbContext. sp_set_session_context was only stamped at open
        // time with A; without a re-stamp this query would silently keep returning A's rows
        // (or, scope-wise worse, B's request reading A's data). The command interceptor must
        // notice the change and re-stamp before executing.
        using (AgencyContext.BeginScope(agencyB.AgencyId))
        {
            var asAgencyB = await context.Customers.AsNoTracking().ToListAsync();
            Assert.Equal(agencyB.CustomerId, Assert.Single(asAgencyB).Id);
        }

        await context.Database.CloseConnectionAsync();
    }

    [Fact]
    public async Task ScopeGuard_rejects_a_reference_to_another_agencys_row()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, agencyB) = await DevSeeder.SeedTwoAgenciesAsync(context);

        AgencyContext.Current = agencyA.AgencyId;
        var guard = new ScopeGuard(context);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => guard.EnsureExistsInScopeAsync<Customer>(agencyB.CustomerId));

        // Sanity check: the same guard accepts a row that genuinely belongs to agency A.
        await guard.EnsureExistsInScopeAsync<Customer>(agencyA.CustomerId);
    }

    [Fact]
    public async Task Duplicate_payment_insert_is_caught_and_treated_as_success()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);
        AgencyContext.Current = agencyA.AgencyId;

        var paidOn = DateOnly.FromDateTime(DateTime.UtcNow);
        var payment = new Payment
        {
            AgencyId = agencyA.AgencyId,
            CustomerId = agencyA.CustomerId,
            InstallmentIdHint = agencyA.InstallmentId,
            Amount = 2_000_000,
            PaidOn = paidOn,
            Method = "Cash",
            RecordedByUserId = Guid.NewGuid(),
        };
        context.Payments.Add(payment);
        await context.SaveChangesAsync();

        // Same (AgencyId, InstallmentIdHint, PaidOn, Amount) — the dedupe index must reject this
        // at the database, and the caller treats that as success rather than an error.
        await using var duplicateContext = TestDbContextFactory.Create();
        AgencyContext.Current = agencyA.AgencyId;
        duplicateContext.Payments.Add(new Payment
        {
            AgencyId = agencyA.AgencyId,
            CustomerId = agencyA.CustomerId,
            InstallmentIdHint = agencyA.InstallmentId,
            Amount = 2_000_000,
            PaidOn = paidOn,
            Method = "Cash",
            RecordedByUserId = Guid.NewGuid(),
        });

        var duplicateWasRejected = false;
        try
        {
            await duplicateContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            duplicateWasRejected = true; // treated as success by the caller — no re-throw
        }

        Assert.True(duplicateWasRejected, "duplicate payment insert should violate the unique dedupe index");

        var paymentCount = await context.Payments.AsNoTracking()
            .CountAsync(p => p.InstallmentIdHint == agencyA.InstallmentId);
        Assert.Equal(1, paymentCount);
    }
}
