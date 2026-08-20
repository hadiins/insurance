using Aqsat.Application.Auth;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Seed;

/// <summary>
/// Minimal fixture proving tenant isolation — NOT the full ~200-policy dataset from
/// docs/PHASE-1-SPEC.md §7 (that's for later tasks whose UI/reports need realistic volume).
/// All data is fake, per CLAUDE.md's "Ask before deciding" list (never real customer data).
/// </summary>
public static class DevSeeder
{
    public sealed record SeededAgency(Guid AgencyId, Guid CustomerId, Guid PolicyId, Guid InstallmentId);

    public static async Task<(SeededAgency AgencyA, SeededAgency AgencyB)> SeedTwoAgenciesAsync(
        AppDbContext context, CancellationToken ct = default)
    {
        await InsuranceLineSeeder.EnsureSeededAsync(context, ct);
        var thirdPartyLineId = await GetThirdPartyLineIdAsync(context, ct);

        var agencyA = await SeedOneAgencyAsync(context, "2491", "شیراز", thirdPartyLineId, ct);
        var agencyB = await SeedOneAgencyAsync(context, "3312", "اصفهان", thirdPartyLineId, ct);
        return (agencyA, agencyB);
    }

    private static async Task<Guid> GetThirdPartyLineIdAsync(AppDbContext context, CancellationToken ct) =>
        await context.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode)
            .Select(l => l.Id)
            .FirstAsync(ct);

    private static async Task<SeededAgency> SeedOneAgencyAsync(
        AppDbContext context, string code, string city, Guid insuranceLineId, CancellationToken ct)
    {
        var org = new Organization
        {
            Level = OrganizationLevel.Agency,
            Code = code,
            Name = $"نمایندگی {code}",
            City = city,
            InsurerName = "شرکت بیمهٔ آزمایشی",
            IsActive = true,
        };
        context.Organizations.Add(org);

        // Id is database-generated (NEWSEQUENTIALID()) and stays Guid.Empty in memory until this
        // save completes — Vehicle/Customer must not read org.Id until after it, since Guid is a
        // value type and an early read would freeze in the empty value forever.
        await context.SaveChangesAsync(ct);

        // AgencyContext must be set to this agency before SaveChanges for the RLS block predicate
        // to allow the insert — this is the seeder proving the same mechanism the app will use.
        AgencyContext.Current = org.Id;

        var vehicle = new Vehicle { AgencyId = org.Id, Plate = "11الف111" };
        var customer = new Customer
        {
            AgencyId = org.Id,
            ExternalCode = $"EXT-{code}",
            FullName = "مشتری آزمایشی",
        };
        context.Vehicles.Add(vehicle);
        context.Customers.Add(customer);
        await context.SaveChangesAsync(ct);

        var policy = new Policy
        {
            AgencyId = org.Id,
            PolicyNumber = $"POL-{code}-0001",
            InsuranceLineId = insuranceLineId,
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            ContractName = "تجارت آفرینان تسنیم",
            IsInstallment = true,
            IssueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            NetPremium = 9_000_000,
            DownPayment = 1_000_000,
            InstallmentCount = 4,
            Status = PolicyStatus.Active,
        };
        context.Policies.Add(policy);
        await context.SaveChangesAsync(ct);

        var installment = new Installment
        {
            AgencyId = org.Id,
            PolicyId = policy.Id,
            SeqNo = 1,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            SettlementDeadline = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1).AddDays(3)),
            Amount = 2_000_000,
            Status = InstallmentStatus.Unpaid,
        };
        context.Installments.Add(installment);
        await context.SaveChangesAsync(ct);

        return new SeededAgency(org.Id, customer.Id, policy.Id, installment.Id);
    }

    /// <summary>Known dev-only password for every seeded user — this is fake fixture data, never real credentials.</summary>
    public const string SeededUserPassword = "Passw0rd!1";

    public sealed record SeededAuthFixture(
        Guid HeadquartersId,
        Guid RegionalId,
        Guid AgencyAId,
        Guid AgencyBId,
        Guid DualAgencyManagerId,
        string DualAgencyManagerMobile,
        Guid AgencyOnlyStaffId,
        string AgencyOnlyStaffMobile);

    /// <summary>
    /// Builds the 3-level Organization tree CLAUDE.md requires from day one (only Agency ships in
    /// Phase 1, but the hierarchy and RLS predicate must already support all three), plus Role/
    /// RolePermission/UserOrgRole fixtures proving Task 4's own check: one user with roles in two
    /// agencies (org switching) and one user missing Payment.Write (403 check).
    /// None of these tables carry AgencyId — Organization is the tenant itself, AppUser/Role/
    /// RolePermission/UserOrgRole are the identity tables Task 3 exempted from RLS — so this needs
    /// no AgencyContext at all, matching the real login flow that must work before any scope exists.
    /// </summary>
    public static async Task<SeededAuthFixture> SeedAuthFixtureAsync(AppDbContext context, CancellationToken ct = default)
    {
        // Role.Name and AppUser.Mobile are globally unique (neither table carries AgencyId, so
        // there's no per-agency scope to make repeats safe the way SeedTwoAgenciesAsync's Customer
        // rows are) — a random suffix keeps this seeder safely re-runnable against a persistent dev
        // database, same spirit as Task 3's seeder relying on a fresh AgencyId per run.
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var hq = new Organization { Level = OrganizationLevel.Headquarters, Code = "HQ", Name = "دفتر مرکزی", IsActive = true };
        context.Organizations.Add(hq);
        await context.SaveChangesAsync(ct);

        var regional = new Organization { Level = OrganizationLevel.Regional, ParentId = hq.Id, Code = "REG-1", Name = "منطقهٔ یک", IsActive = true };
        context.Organizations.Add(regional);
        await context.SaveChangesAsync(ct);

        var agencyA = new Organization { Level = OrganizationLevel.Agency, ParentId = regional.Id, Code = "4001", Name = "نمایندگی ۴۰۰۱", City = "تهران", InsurerName = "شرکت بیمهٔ آزمایشی", IsActive = true };
        var agencyB = new Organization { Level = OrganizationLevel.Agency, ParentId = regional.Id, Code = "4002", Name = "نمایندگی ۴۰۰۲", City = "مشهد", InsurerName = "شرکت بیمهٔ آزمایشی", IsActive = true };
        context.Organizations.AddRange(agencyA, agencyB);
        await context.SaveChangesAsync(ct);

        var managerRole = new Role { Name = $"Manager-{suffix}" };
        var staffRole = new Role { Name = $"Staff-{suffix}" };
        context.Roles.AddRange(managerRole, staffRole);
        await context.SaveChangesAsync(ct);

        foreach (var permission in Permissions.All)
        {
            context.RolePermissions.Add(new RolePermission { RoleId = managerRole.Id, Permission = permission });
        }

        foreach (var permission in Permissions.All.Where(p => p is not (Permissions.PaymentWrite or Permissions.SettingsWrite)))
        {
            context.RolePermissions.Add(new RolePermission { RoleId = staffRole.Id, Permission = permission });
        }

        await context.SaveChangesAsync(ct);

        var hasher = new PasswordHasher();
        var passwordHash = hasher.Hash(SeededUserPassword);

        // Mobile is globally unique — random 8-digit suffix keeps re-runs collision-free in practice.
        var dualAgencyManagerMobile = $"091{Random.Shared.Next(10_000_000, 99_999_999)}";
        var agencyOnlyStaffMobile = $"091{Random.Shared.Next(10_000_000, 99_999_999)}";

        var dualAgencyManager = new AppUser { FullName = "مدیر دونمایندگی", Mobile = dualAgencyManagerMobile, PasswordHash = passwordHash, IsActive = true };
        var agencyOnlyStaff = new AppUser { FullName = "کارمند بدون دسترسی پرداخت", Mobile = agencyOnlyStaffMobile, PasswordHash = passwordHash, IsActive = true };
        context.Users.AddRange(dualAgencyManager, agencyOnlyStaff);
        await context.SaveChangesAsync(ct);

        // Deliberately a DIFFERENT role per agency for the dual-agency user — Task 4's check is
        // "two roles in two agencies", not just two memberships. Manager in A, Staff in B proves
        // permissions actually change with the active org, not merely ActiveOrganizationId.
        context.UserOrgRoles.AddRange(
            new UserOrgRole { UserId = dualAgencyManager.Id, OrganizationId = agencyA.Id, RoleId = managerRole.Id },
            new UserOrgRole { UserId = dualAgencyManager.Id, OrganizationId = agencyB.Id, RoleId = staffRole.Id },
            new UserOrgRole { UserId = agencyOnlyStaff.Id, OrganizationId = agencyA.Id, RoleId = staffRole.Id });
        await context.SaveChangesAsync(ct);

        return new SeededAuthFixture(
            hq.Id, regional.Id, agencyA.Id, agencyB.Id,
            dualAgencyManager.Id, dualAgencyManager.Mobile,
            agencyOnlyStaff.Id, agencyOnlyStaff.Mobile);
    }
}
