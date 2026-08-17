using System.Reflection;
using Aqsat.Domain;
using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Auth;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Audit;

/// <summary>
/// Runs against a real local SQL Server database, same as RowLevelSecurityTests — proves
/// AppDbContext.SaveChangesAsync's audit override (Task 5), not a mocked persistence layer.
/// </summary>
public class AuditInfrastructureTests
{
    [Fact]
    public async Task Seeding_a_policy_and_installment_produces_Created_audit_rows_with_correct_PolicyId()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);
        AgencyContext.Current = agencyA.AgencyId;

        var policyAudit = await context.AuditEntries
            .SingleAsync(a => a.EntityId == agencyA.PolicyId && a.EntityType == nameof(Policy));
        Assert.Equal(AuditAction.Created, policyAudit.Action);
        Assert.Equal(agencyA.PolicyId, policyAudit.PolicyId);
        Assert.Null(policyAudit.ChangesJson);

        var installmentAudit = await context.AuditEntries
            .SingleAsync(a => a.EntityId == agencyA.InstallmentId && a.EntityType == nameof(Installment));
        Assert.Equal(AuditAction.Created, installmentAudit.Action);
        // PolicyId is resolved from Installment.PolicyId, not the installment's own Id — this is
        // the actual substance of CLAUDE.md rule 28.
        Assert.Equal(agencyA.PolicyId, installmentAudit.PolicyId);
    }

    [Fact]
    public async Task Modifying_a_policy_produces_one_audit_row_with_the_correct_PolicyId_and_description()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);
        AgencyContext.Current = agencyA.AgencyId;
        CurrentUserContext.Current = new ResolvedUser(Guid.NewGuid(), "کاربر آزمایشی", agencyA.AgencyId, new HashSet<string>());

        var policy = await context.Policies.SingleAsync(p => p.Id == agencyA.PolicyId);
        policy.PolicyNumber = "POL-UPDATED-0001";
        // Same production code path the SaveChangesAsync override itself calls — comparing against
        // this instead of a hand-typed Persian literal avoids an invisible-diacritic mismatch
        // between this file and Policy.cs.
        var expectedDescription = ((IAuditableEntity)policy).DescribeChange(AuditAction.Updated);
        await context.SaveChangesAsync();

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = agencyA.AgencyId;
        var updateRows = await verify.AuditEntries
            .Where(a => a.EntityId == agencyA.PolicyId && a.Action == AuditAction.Updated)
            .ToListAsync();

        Assert.Single(updateRows);
        Assert.Equal(agencyA.PolicyId, updateRows[0].PolicyId);
        Assert.Equal(expectedDescription, updateRows[0].Description);
        Assert.Contains("PolicyNumber", updateRows[0].ChangesJson);
    }

    /// <summary>
    /// A DisplayName longer than AuditEntry.UserDisplayName's NVARCHAR(120) column forces the
    /// *second* SaveChanges inside the override (the audit insert) to fail at the database — this
    /// specifically proves the two-phase design is atomic, not just that a failed single INSERT
    /// rolls back on its own.
    /// </summary>
    [Fact]
    public async Task An_exception_during_the_audit_phase_rolls_back_the_original_change_too()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);
        AgencyContext.Current = agencyA.AgencyId;

        var originalPolicyNumber = (await context.Policies.AsNoTracking()
            .SingleAsync(p => p.Id == agencyA.PolicyId)).PolicyNumber;

        CurrentUserContext.Current = new ResolvedUser(
            Guid.NewGuid(), new string('ا', 200), agencyA.AgencyId, new HashSet<string>());

        var trackedPolicy = await context.Policies.SingleAsync(p => p.Id == agencyA.PolicyId);
        trackedPolicy.PolicyNumber = "CHANGED-SHOULD-NOT-PERSIST";

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync());

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = agencyA.AgencyId;

        var reloadedPolicy = await verify.Policies.AsNoTracking().SingleAsync(p => p.Id == agencyA.PolicyId);
        Assert.Equal(originalPolicyNumber, reloadedPolicy.PolicyNumber);

        // Only the seeder's own Created row should exist — the failed Update attempt above must
        // not have left a partial audit row behind either.
        var auditCount = await verify.AuditEntries.CountAsync(a => a.EntityId == agencyA.PolicyId);
        Assert.Equal(1, auditCount);
    }

    [Fact]
    public void Customer_NationalId_is_marked_AuditSensitive()
    {
        var property = typeof(Customer).GetProperty(nameof(Customer.NationalId));
        Assert.NotNull(property!.GetCustomAttribute<AuditSensitiveAttribute>());
    }
}
