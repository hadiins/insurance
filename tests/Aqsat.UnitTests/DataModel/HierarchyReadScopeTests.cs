using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.DataModel;

/// <summary>
/// Covers the widened FILTER predicate from migration AddAgencyHierarchyReadScope
/// (fn_AgencySubtree / fn_AgencyReadPredicate) against the real local SQL Server, the same
/// database RowLevelSecurityTests runs against.
///
/// Every fixture builds a FRESH organization tree — a brand-new Headquarters with two Regional
/// children, one of which owns an Agency and a sub-Agency. That matters for the assertions: the
/// shared test database accumulates thousands of other agencies, none of which can possibly be
/// inside a subtree whose roots were created microseconds ago, so "nothing outside my subtree is
/// visible" is a check that can genuinely fail rather than one padded by filtering to known ids.
/// </summary>
public class HierarchyReadScopeTests
{
    private sealed record Fixture(
        Guid RegionId,
        Guid ChildAgencyId,
        Guid GrandchildAgencyId,
        Guid OutsideAgencyId,
        // ISet, not IReadOnlySet: xUnit's Assert.Subset takes the expected subset as an ISet.
        ISet<Guid> RegionSubtree,
        ISet<Guid> ChildSubtree);

    [Fact]
    public async Task A_regional_tenant_sees_the_subtree_only_when_the_scope_is_widened()
    {
        await using var context = TestDbContextFactory.Create();
        var fixture = await SeedHierarchyAsync(context);

        // Default (strict): the region is a tenant like any other and reads exactly its own rows.
        using (AgencyContext.BeginScope(fixture.RegionId))
        {
            var strict = await VisibleAgencyIdsAsync(context);
            Assert.Equal(new[] { fixture.RegionId }, strict);
        }

        // Widened: own rows plus every descendant at every depth, and nothing from the
        // neighbouring subtree hanging off the same Headquarters.
        using (AgencyContext.BeginScope(fixture.RegionId, hierarchyRead: true))
        {
            var widened = (await VisibleAgencyIdsAsync(context)).ToHashSet();

            Assert.Contains(fixture.RegionId, widened);
            Assert.Contains(fixture.ChildAgencyId, widened);
            Assert.Contains(fixture.GrandchildAgencyId, widened);
            Assert.DoesNotContain(fixture.OutsideAgencyId, widened);
            Assert.Subset(fixture.RegionSubtree, widened);
        }
    }

    [Fact]
    public async Task Widened_scope_on_a_leaf_agency_never_reaches_upward_or_sideways()
    {
        await using var context = TestDbContextFactory.Create();
        var fixture = await SeedHierarchyAsync(context);

        // The header a caller can send is not an authorization decision about direction: the
        // subtree is downward-only, so a leaf that asks for it still cannot see its own parent.
        using (AgencyContext.BeginScope(fixture.ChildAgencyId, hierarchyRead: true))
        {
            var widened = (await VisibleAgencyIdsAsync(context)).ToHashSet();

            Assert.Contains(fixture.ChildAgencyId, widened);
            Assert.Contains(fixture.GrandchildAgencyId, widened); // downward is the whole point
            Assert.DoesNotContain(fixture.RegionId, widened);        // never upward
            Assert.DoesNotContain(fixture.OutsideAgencyId, widened); // never sideways
            Assert.Subset(fixture.ChildSubtree, widened);
        }
    }

    [Fact]
    public async Task Widened_read_still_cannot_write_into_a_subordinate_agencys_rows()
    {
        await using var context = TestDbContextFactory.Create();
        var fixture = await SeedHierarchyAsync(context);

        using (AgencyContext.BeginScope(fixture.RegionId, hierarchyRead: true))
        {
            // Readable is not writable. The policy widens FILTER predicates only; the BLOCK
            // predicates are still strict equality against the stamped AgencyId, so this insert
            // is rejected by the database rather than by application-side discipline.
            context.Customers.Add(new Customer
            {
                AgencyId = fixture.ChildAgencyId,
                ExternalCode = $"EXT-{Guid.NewGuid():N}"[..20],
                FullName = "مشتری آزمایشی",
            });

            var failure = await Assert.ThrowsAsync<DbUpdateException>(
                () => context.SaveChangesAsync());

            Assert.IsType<SqlException>(failure.InnerException);
            Assert.Contains("block predicate", failure.InnerException!.Message,
                StringComparison.OrdinalIgnoreCase);
        }

        // The rejection left nothing behind, and the child's own row is still exactly one.
        using (AgencyContext.BeginScope(fixture.ChildAgencyId))
        {
            var childCustomerCount = await context.Customers.AsNoTracking()
                .CountAsync(c => c.AgencyId == fixture.ChildAgencyId);
            Assert.Equal(1, childCustomerCount);
        }
    }

    [Fact]
    public async Task Held_open_connection_re_stamps_when_only_the_hierarchy_flag_changes()
    {
        await using var context = TestDbContextFactory.Create();
        var fixture = await SeedHierarchyAsync(context);

        using (AgencyContext.BeginScope(fixture.RegionId))
        {
            await context.Database.OpenConnectionAsync();
            Assert.Equal(new[] { fixture.RegionId }, await VisibleAgencyIdsAsync(context));
        }

        // Same physical connection, still open, same AgencyId — only the widening flag flipped.
        // The interceptor caches (AgencyId, HierarchyRead) as one pair; had it cached the agency
        // alone, this query would silently keep returning the strict result set.
        using (AgencyContext.BeginScope(fixture.RegionId, hierarchyRead: true))
        {
            var widened = (await VisibleAgencyIdsAsync(context)).ToHashSet();
            Assert.Contains(fixture.ChildAgencyId, widened);
            Assert.Contains(fixture.GrandchildAgencyId, widened);
        }

        await context.Database.CloseConnectionAsync();
    }

    /// <summary>Distinct set of tenant ids the CURRENT ambient scope can see in Customers — an
    /// unfiltered query, so RLS is the only thing deciding the result.</summary>
    private static async Task<List<Guid>> VisibleAgencyIdsAsync(AppDbContext context) =>
        await context.Customers.AsNoTracking().Select(c => c.AgencyId).Distinct().ToListAsync();

    private static async Task<Fixture> SeedHierarchyAsync(AppDbContext context)
    {
        var headquarters = await AddOrganizationAsync(context, OrganizationLevel.Headquarters, null);
        var region = await AddOrganizationAsync(context, OrganizationLevel.Regional, headquarters);
        var outsideRegion = await AddOrganizationAsync(context, OrganizationLevel.Regional, headquarters);
        var childAgency = await AddOrganizationAsync(context, OrganizationLevel.Agency, region);
        var grandchildAgency = await AddOrganizationAsync(context, OrganizationLevel.Agency, childAgency);
        var outsideAgency = await AddOrganizationAsync(context, OrganizationLevel.Agency, outsideRegion);

        await AddCustomerAsync(context, region);
        await AddCustomerAsync(context, childAgency);
        await AddCustomerAsync(context, grandchildAgency);
        await AddCustomerAsync(context, outsideAgency);

        return new Fixture(
            region, childAgency, grandchildAgency, outsideAgency,
            new HashSet<Guid> { region, childAgency, grandchildAgency },
            new HashSet<Guid> { childAgency, grandchildAgency });
    }

    private static async Task<Guid> AddOrganizationAsync(
        AppDbContext context, OrganizationLevel level, Guid? parentId)
    {
        // Organizations is the tenant table itself: no AgencyId column, therefore not RLS-scoped.
        var organization = new Organization
        {
            Level = level,
            ParentId = parentId,
            Code = $"{(int)level}-{Guid.NewGuid():N}"[..12],
            Name = "سازمان آزمایشی",
            IsActive = true,
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        // Id is database-generated (NEWSEQUENTIALID), so it only reads back non-empty after the save.
        return organization.Id;
    }

    private static async Task AddCustomerAsync(AppDbContext context, Guid agencyId)
    {
        // Inserting on a tenant's behalf requires that tenant's BLOCK predicate, i.e. the scope.
        using (AgencyContext.BeginScope(agencyId))
        {
            context.Customers.Add(new Customer
            {
                AgencyId = agencyId,
                ExternalCode = $"EXT-{Guid.NewGuid():N}"[..20],
                FullName = "مشتری آزمایشی",
            });
            await context.SaveChangesAsync();
        }
    }

}
