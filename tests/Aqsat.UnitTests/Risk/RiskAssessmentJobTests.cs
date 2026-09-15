using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Jobs;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Risk;
using Aqsat.Infrastructure.Seed;
using Aqsat.Infrastructure.Security;
using Aqsat.UnitTests.DataModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aqsat.UnitTests.Risk;

/// <summary>
/// Phase 2A stage 4 — the nightly job walks every agency and re-assesses its policy-holding
/// customers with Source = Scheduled, writing the same audit row a manual run would (rule 29).
/// In the suite's collection so it never runs in parallel with endpoint tests that count a
/// customer's assessments — the job walks the WHOLE shared test database, not just this fixture.
/// </summary>
[Collection("WebApplicationFactory")]
public class RiskAssessmentJobTests
{
    private static IFieldEncryptor BuildFieldEncryptor()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:NationalIdKey"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
            })
            .Build();
        return new AesFieldEncryptor(configuration);
    }

    private static string UniqueNationalId()
    {
        var digits = $"008{Random.Shared.Next(1_000_000):D6}";
        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (digits[i] - '0') * (10 - i);
        }
        var remainder = sum % 11;
        return digits + (remainder < 2 ? remainder : 11 - remainder);
    }

    private static string UniquePlate() =>
        $"{Random.Shared.Next(10, 99)}د{Random.Shared.Next(100, 999)}-{Random.Shared.Next(10, 99)}";

    private static async Task<(Guid AgencyId, string PlateNormalized)> SeedAgencyWithCustomerAsync(AppDbContext context)
    {
        var fixture = await DevSeeder.SeedAuthFixtureAsync(context);
        await InsuranceLineSeeder.EnsureSeededAsync(context);
        var lineId = await context.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode)
            .Select(l => l.Id)
            .FirstAsync();

        // The RLS block predicate refuses inserts into Customers without the agency scope set.
        AgencyContext.Current = fixture.AgencyAId;

        var customer = new Customer
        {
            AgencyId = fixture.AgencyAId,
            ExternalCode = $"J4-{Guid.NewGuid():N}"[..16],
            FullName = "مشتری جاب شبانه",
            NationalId = UniqueNationalId(),
            Mobile = $"091{Random.Shared.Next(10_000_000, 99_999_999)}",
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var plateNormalized = UniquePlate();
        var vehicle = new Vehicle
        {
            AgencyId = fixture.AgencyAId,
            Plate = "۵۵ الف ۵۵۵ ایران ۵۵",
            PlateNormalized = plateNormalized,
        };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        var policy = new Policy
        {
            AgencyId = fixture.AgencyAId,
            PolicyNumber = $"J4-{Guid.NewGuid():N}"[..16],
            InsuranceLineId = lineId,
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            ContractName = "تجارت آفرینان تسنیم",
            IsInstallment = true,
            IssueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-2)),
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-2)),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(10)),
            NetPremium = 9_000_000,
            DownPayment = 0,
            InstallmentCount = 3,
            Status = PolicyStatus.Active,
        };
        context.Policies.Add(policy);
        await context.SaveChangesAsync();

        context.Installments.Add(new Installment
        {
            AgencyId = fixture.AgencyAId,
            PolicyId = policy.Id,
            SeqNo = 1,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            SettlementDeadline = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1).AddDays(3)),
            Amount = 3_000_000,
            Status = InstallmentStatus.Unpaid,
        });
        await context.SaveChangesAsync();

        return (fixture.AgencyAId, plateNormalized);
    }

    [Fact]
    public async Task The_nightly_run_assesses_every_policy_holder_as_scheduled()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyId, _) = await SeedAgencyWithCustomerAsync(context);

        var job = new RiskAssessmentJob(
            context,
            new RiskAssessmentService(context, new RiskFeatureCalculator(context), BuildFieldEncryptor(), TimeProvider.System),
            NullLogger<RiskAssessmentJob>.Instance);
        await job.RunAsync();

        AgencyContext.Current = agencyId;
        var assessments = await context.RiskAssessments.AsNoTracking().ToListAsync();
        Assert.Contains(assessments, a => a.Source == RiskAssessmentSource.Scheduled);
        Assert.DoesNotContain(assessments, a => a.Source == RiskAssessmentSource.Manual);

        Assert.True(await context.AuditEntries.AsNoTracking().AnyAsync(a =>
            a.AgencyId == agencyId
            && a.EntityType == "RiskAssessment"
            && a.UserDisplayName == "زمان‌بندی شبانهٔ سیستم"));
    }

    [Fact]
    public async Task A_customer_without_any_policy_is_left_alone()
    {
        await using var context = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(context);

        var job = new RiskAssessmentJob(
            context,
            new RiskAssessmentService(context, new RiskFeatureCalculator(context), BuildFieldEncryptor(), TimeProvider.System),
            NullLogger<RiskAssessmentJob>.Instance);
        await job.RunAsync();

        AgencyContext.Current = fixture.AgencyAId;
        Assert.False(await context.RiskAssessments.AsNoTracking()
            .AnyAsync(a => a.AgencyId == fixture.AgencyAId));
    }

    /// <summary>Phase 2B-1 — the nightly run also syncs the plate index: the assessed customer's
    /// vehicle plate lands in NetworkRiskPlateIndex keyed by their national-ID hash, which is what
    /// the cross-agency plate lookup searches.</summary>
    [Fact]
    public async Task The_nightly_run_indexes_the_plates_of_assessed_customers()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyId, plateNormalized) = await SeedAgencyWithCustomerAsync(context);

        var job = new RiskAssessmentJob(
            context,
            new RiskAssessmentService(context, new RiskFeatureCalculator(context), BuildFieldEncryptor(), TimeProvider.System),
            NullLogger<RiskAssessmentJob>.Instance);
        await job.RunAsync();

        AgencyContext.Current = agencyId;
        var profile = await context.NetworkRiskProfiles.AsNoTracking()
            .Where(p => p.AgencyId == agencyId)
            .FirstAsync();
        Assert.NotNull(profile);

        var plateRow = await context.NetworkRiskPlateIndex.AsNoTracking()
            .Where(i => i.AgencyId == agencyId && i.PlateNormalized == plateNormalized)
            .FirstAsync();
        Assert.NotNull(plateRow);
        Assert.Equal(profile.NationalIdHash, plateRow.NationalIdHash);
    }
}
