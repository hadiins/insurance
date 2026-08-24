using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Commission;

/// <summary>
/// Stage 3/7 of the accounting buildout: AgencyCommissionEntry mirrors CommissionEntry (marketer's
/// own slice) minus the MarketerId dimension — generated at schedule time for installment policies,
/// activated per-installment on settlement/reversal, and via a single full-policy slice + the new
/// record-full-payment endpoint for non-installment policies.
/// </summary>
[Collection("WebApplicationFactory")]
public class AgencyCommissionEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AgencyCommissionEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Scheduling_generates_slices_from_the_locked_rate_and_settlement_flips_exactly_one_payable()
    {
        var (fixture, seedContext, client) = await SeedAsync();
        var salisLineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var issueDate = new DateOnly(2026, 1, 1);
        var rateResponse = await client.PostAsJsonAsync(
            "/api/settings/agency-commission-rates", new SetAgencyCommissionRateRequest(salisLineId, 10m, issueDate.AddDays(-30)));
        rateResponse.EnsureSuccessStatusCode();

        var policyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-AC-{Guid.NewGuid():N}"[..16], salisLineId, null, "مشتری کارمزد نمایندگی", null, null,
            Vehicle: new VehicleInput("۱۳ب۱۳۰", null, null, null, null, null),
            Property: null,
            issueDate, issueDate, issueDate.AddYears(1),
            10_000_000m, 0m, null, null, false));
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        var scheduleResponse = await client.PostAsJsonAsync(
            $"/api/policies/{policy!.PolicyId}/schedule", new ScheduleRequest(2_000_000m, 2));
        scheduleResponse.EnsureSuccessStatusCode();

        AgencyContext.Current = fixture.AgencyAId;

        // Down-payment slice (20% of receivable) + 2 installment slices (each 40%), 10% of NetPremium = 1,000,000 total.
        var entries = await seedContext.AgencyCommissionEntries.AsNoTracking()
            .Where(e => e.PolicyId == policy.PolicyId).ToListAsync();
        Assert.Equal(3, entries.Count);
        Assert.Equal(1_000_000m, entries.Sum(e => e.Amount));
        var downSlice = Assert.Single(entries, e => e.InstallmentId == null);
        Assert.Equal(CommissionStatus.Payable, downSlice.Status);
        Assert.Equal(2, entries.Count(e => e.Status == CommissionStatus.Pending));

        var firstInstallmentId = await seedContext.Installments
            .Where(i => i.PolicyId == policy.PolicyId).OrderBy(i => i.SeqNo).Select(i => i.Id).FirstAsync();
        var paymentResponse = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            firstInstallmentId, 4_000_000m, issueDate, "Cash", null));
        paymentResponse.EnsureSuccessStatusCode();
        var paymentResult = await paymentResponse.Content.ReadFromJsonAsync<PaymentResultDto>();

        var afterSettlement = await seedContext.AgencyCommissionEntries.AsNoTracking()
            .Where(e => e.PolicyId == policy.PolicyId).ToListAsync();
        Assert.Equal(2, afterSettlement.Count(e => e.Status == CommissionStatus.Payable));
        Assert.Equal(1, afterSettlement.Count(e => e.Status == CommissionStatus.Pending));

        // Reversing the payment must undo the flip, same as it already does for CommissionEntry.
        var reverseResponse = await client.PostAsync($"/api/payments/{paymentResult!.PaymentId}/reverse", null);
        reverseResponse.EnsureSuccessStatusCode();

        var afterReversal = await seedContext.AgencyCommissionEntries.AsNoTracking()
            .Where(e => e.PolicyId == policy.PolicyId).ToListAsync();
        Assert.Equal(1, afterReversal.Count(e => e.Status == CommissionStatus.Payable)); // only the down-payment slice
        Assert.Equal(2, afterReversal.Count(e => e.Status == CommissionStatus.Pending));
    }

    [Fact]
    public async Task Recording_a_full_payment_on_a_non_installment_policy_creates_and_pays_the_single_slice()
    {
        var (fixture, seedContext, client) = await SeedAsync();
        var salisLineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        AgencyContext.Current = fixture.AgencyAId;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var customer = new Customer { AgencyId = fixture.AgencyAId, ExternalCode = $"EXT-{Guid.NewGuid():N}"[..12], FullName = "مشتری غیراقساطی" };
        var vehicle = new Vehicle { AgencyId = fixture.AgencyAId, Plate = "55و555" };
        seedContext.Customers.Add(customer);
        seedContext.Vehicles.Add(vehicle);
        await seedContext.SaveChangesAsync();

        var policy = new Policy
        {
            AgencyId = fixture.AgencyAId,
            PolicyNumber = $"POL-FULL-{Guid.NewGuid():N}"[..16],
            InsuranceLineId = salisLineId,
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            ContractName = "شرکت بیمهٔ عادی",
            IsInstallment = false,
            IssueDate = today,
            StartDate = today,
            EndDate = today.AddYears(1),
            NetPremium = 5_000_000m,
            DownPayment = 0,
            InstallmentCount = 0,
            AgencyCommissionPercent = 8m,
        };
        seedContext.Policies.Add(policy);
        await seedContext.SaveChangesAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/policies/{policy.Id}/record-full-payment",
            new RecordFullPaymentRequest(5_000_000m, today, "Cash", null));
        response.EnsureSuccessStatusCode();

        var entry = await seedContext.AgencyCommissionEntries.AsNoTracking().SingleAsync(e => e.PolicyId == policy.Id);
        Assert.True(entry.IsFullPolicySlice);
        Assert.Null(entry.InstallmentId);
        Assert.Equal(CommissionStatus.Payable, entry.Status);
        Assert.Equal(400_000m, entry.Amount); // 5,000,000 * 8%
        Assert.NotNull(entry.EligibleAt);

        var payment = await seedContext.Payments.AsNoTracking().SingleAsync(p => p.InstallmentIdHint == policy.Id);
        Assert.Equal(5_000_000m, payment.Amount);

        // Idempotent resubmission must not create a second slice or a second payment.
        var secondResponse = await client.PostAsJsonAsync(
            $"/api/policies/{policy.Id}/record-full-payment",
            new RecordFullPaymentRequest(5_000_000m, today, "Cash", null));
        secondResponse.EnsureSuccessStatusCode();
        Assert.Equal(1, await seedContext.Payments.AsNoTracking().CountAsync(p => p.InstallmentIdHint == policy.Id));
        Assert.Equal(1, await seedContext.AgencyCommissionEntries.AsNoTracking().CountAsync(e => e.PolicyId == policy.Id));
    }

    private async Task<(DevSeeder.SeededAuthFixture Fixture, AppDbContext SeedContext, HttpClient Client)> SeedAsync()
    {
        var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        return (fixture, seedContext, client);
    }
}
