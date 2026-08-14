using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;

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
        var agencyA = await SeedOneAgencyAsync(context, "2491", "شیراز", ct);
        var agencyB = await SeedOneAgencyAsync(context, "3312", "اصفهان", ct);
        return (agencyA, agencyB);
    }

    private static async Task<SeededAgency> SeedOneAgencyAsync(
        AppDbContext context, string code, string city, CancellationToken ct)
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
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            ContractName = "تجارت آفرینان تسنیم",
            IsInstallment = true,
            IssueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            TotalPremium = 9_000_000,
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
}
