using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Application.Schedule;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Schedule;

/// <summary>
/// End-to-end over real HTTP (same pattern as AuthenticationTests) — proves the schedule endpoint
/// is actually wired to InstallmentAmountCalculator/DueDateCalculator, RLS, and the Policy.Write
/// permission, not just that the calculators themselves are correct in isolation.
/// </summary>
[Collection("WebApplicationFactory")]
public class PolicyScheduleEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PolicyScheduleEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Scheduling_a_policy_creates_installments_matching_the_specs_worked_example()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);
        var thirdPartyLineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var policy = new Policy
        {
            AgencyId = fixture.AgencyAId,
            PolicyNumber = $"POL-{Guid.NewGuid():N}"[..16],
            InsuranceLineId = thirdPartyLineId,
            CustomerId = Guid.Empty,
            VehicleId = Guid.Empty,
            ContractName = "تجارت آفرینان تسنیم",
            IsInstallment = true,
            IssueDate = new DateOnly(2026, 1, 1),
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2027, 1, 1),
            NetPremium = 10_700_000m,
            DownPayment = 0,
            InstallmentCount = 0,
        };

        AgencyContext.Current = fixture.AgencyAId;
        var customer = new Customer { AgencyId = fixture.AgencyAId, ExternalCode = "EXT-1", FullName = "مشتری آزمایشی" };
        var vehicle = new Vehicle { AgencyId = fixture.AgencyAId, Plate = "11الف111" };
        seedContext.Customers.Add(customer);
        seedContext.Vehicles.Add(vehicle);
        await seedContext.SaveChangesAsync();
        policy.CustomerId = customer.Id;
        policy.VehicleId = vehicle.Id;
        seedContext.Policies.Add(policy);
        await seedContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        var response = await client.PostAsJsonAsync(
            $"/api/policies/{policy.Id}/schedule", new ScheduleRequest(1_700_000m, 9));
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ScheduleResultDto>();
        Assert.NotNull(result);
        Assert.False(result!.ExceedsMaxInstallments);
        Assert.Equal(9, result.Installments.Count);
        Assert.All(result.Installments, i => Assert.Equal(1_000_000m, i.Amount));
        // docs/TASK-25-IDENTITY-VEHICLE.md §6.2 — due dates are Jalali-month math now, not
        // Gregorian, so the expected values are derived the same way the endpoint computes them
        // rather than hand-coded Gregorian literals that would silently re-encode the old behavior.
        Assert.Equal(DueDateCalculator.CalculateDueDate(policy.StartDate, 1), result.Installments[0].DueDate);
        Assert.Equal(DueDateCalculator.CalculateDueDate(policy.StartDate, 9), result.Installments[8].DueDate);

        AgencyContext.Current = fixture.AgencyAId;
        var persistedCount = await seedContext.Installments.AsNoTracking().CountAsync(i => i.PolicyId == policy.Id);
        Assert.Equal(9, persistedCount);

        // A down payment is collected up front and never allocated against any installment — it
        // gets its own settled Payment (receipt) instead, with no PaymentAllocation rows.
        var downPaymentReceipt = await seedContext.Payments.AsNoTracking()
            .Include(p => p.Allocations)
            .SingleAsync(p => p.InstallmentIdHint == policy.Id);
        Assert.Equal(1_700_000m, downPaymentReceipt.Amount);
        Assert.Equal(policy.IssueDate, downPaymentReceipt.PaidOn);
        Assert.Equal(customer.Id, downPaymentReceipt.CustomerId);
        Assert.Empty(downPaymentReceipt.Allocations);
    }

    /// <summary>docs/PHASE-1-SPEC.md §3.6 — the down payment's ServiceFee share must show up under
    /// cash basis on the day it was collected, not stay invisible until the first real installment
    /// payment (the gap ReportsController's own comment used to flag before the fix).</summary>
    [Fact]
    public async Task Down_payment_is_recognized_in_cash_basis_pnl_on_the_day_it_was_collected()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);
        var thirdPartyLineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var policy = new Policy
        {
            AgencyId = fixture.AgencyAId,
            PolicyNumber = $"POL-{Guid.NewGuid():N}"[..16],
            InsuranceLineId = thirdPartyLineId,
            CustomerId = Guid.Empty,
            VehicleId = Guid.Empty,
            ContractName = "تجارت آفرینان تسنیم",
            IsInstallment = true,
            IssueDate = new DateOnly(2026, 2, 1),
            StartDate = new DateOnly(2026, 2, 1),
            EndDate = new DateOnly(2027, 2, 1),
            NetPremium = 9_000_000m,
            ServiceFee = 1_000_000m,
            DownPayment = 0,
            InstallmentCount = 0,
        };

        AgencyContext.Current = fixture.AgencyAId;
        var customer = new Customer { AgencyId = fixture.AgencyAId, ExternalCode = "EXT-2", FullName = "مشتری آزمایشی ۲" };
        var vehicle = new Vehicle { AgencyId = fixture.AgencyAId, Plate = "22ب222" };
        seedContext.Customers.Add(customer);
        seedContext.Vehicles.Add(vehicle);
        await seedContext.SaveChangesAsync();
        policy.CustomerId = customer.Id;
        policy.VehicleId = vehicle.Id;
        seedContext.Policies.Add(policy);
        await seedContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        // 2,000,000 down payment against a 10,000,000 TotalReceivable is a 20% share.
        var response = await client.PostAsJsonAsync($"/api/policies/{policy.Id}/schedule", new ScheduleRequest(2_000_000m, 5));
        response.EnsureSuccessStatusCode();

        var pnlResponse = await client.GetAsync(
            $"/api/reports/pnl?from={policy.IssueDate:yyyy-MM-dd}&to={policy.IssueDate:yyyy-MM-dd}&basis=Cash");
        pnlResponse.EnsureSuccessStatusCode();
        var pnl = await pnlResponse.Content.ReadFromJsonAsync<PnlResultDto>();

        Assert.NotNull(pnl);
        Assert.Equal(200_000m, pnl!.ServiceFeeIncome);
    }
}
