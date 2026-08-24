using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Reports;

/// <summary>
/// Task 15's own check (docs/TASKS.md): hand-compute one month from seed data and match it to the
/// rial; flip accrual to cash and the numbers change coherently, both defensible.
///
/// Worked example: NetPremium 10,000,000, ServiceFee 0, agency commission 10% (=1,000,000),
/// marketer rate 10% (totalCommission=1,000,000), down payment 2,000,000, 2 installments of
/// 4,000,000 each. Commission slices: down 200,000 (payable immediately), each installment 400,000
/// (pending until settled).
/// </summary>
[Collection("WebApplicationFactory")]
public class ProfitAndLossEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProfitAndLossEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    // DateOnly's default ToString/interpolation formats using CurrentCulture's calendar — on a
    // Persian-locale host (this app's actual deployment target) that renders Jalali digits, which
    // ASP.NET Core's [FromQuery] DateOnly binder does not parse back correctly. Query strings must
    // always be built with an explicit invariant format, matching what the real frontend's
    // <input type="date"> already sends.
    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Accrual_and_cash_bases_both_hand_compute_to_the_rial_and_diverge_coherently_after_one_installment_settles()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);
        var salisLineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var marketerResponse = await client.PostAsJsonAsync(
            "/api/marketers", new CreateMarketerRequest("بازاریاب سود و زیان", "09129990000", null, "Independent", null));
        marketerResponse.EnsureSuccessStatusCode();
        var marketer = await marketerResponse.Content.ReadFromJsonAsync<MarketerDto>();

        var rateResponse = await client.PostAsJsonAsync(
            $"/api/marketers/{marketer!.Id}/rates", new SetMarketerRateRequest(salisLineId, 10m, today.AddDays(-30)));
        rateResponse.EnsureSuccessStatusCode();

        var policyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-PNL-{Guid.NewGuid():N}"[..16], salisLineId, null, "مشتری سود و زیان", null, null,
            Vehicle: new VehicleInput("۹۹ب۹۹۹", null, null, null, null, null),
            Property: null,
            today, today, today.AddYears(1),
            NetPremium: 10_000_000m, ServiceFee: 0m, MarketerId: marketer.Id, PreviousInsurer: null, IsRenewal: false,
            AgencyCommissionPercent: 10m));
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        var scheduleResponse = await client.PostAsJsonAsync(
            $"/api/policies/{policy!.PolicyId}/schedule", new ScheduleRequest(2_000_000m, 2));
        scheduleResponse.EnsureSuccessStatusCode();
        var schedule = await scheduleResponse.Content.ReadFromJsonAsync<ScheduleResultDto>();

        // --- Snapshot 1: right after schedule generation, only the down-payment slice is payable. ---
        var accrualBefore = await client.GetFromJsonAsync<PnlResultDto>(
            $"/api/reports/pnl?from={Iso(today)}&to={Iso(today)}&basis=Accrual");
        Assert.NotNull(accrualBefore);
        Assert.Equal(1_000_000m, accrualBefore!.AgencyCommissionIncome);
        Assert.Equal(0m, accrualBefore.ServiceFeeIncome);
        Assert.Equal(1_000_000m, accrualBefore.TotalIncome);
        Assert.Equal(200_000m, accrualBefore.MarketerCommissionExpense);
        Assert.Equal(0m, accrualBefore.DefaultWriteOffExpense);
        Assert.Equal(200_000m, accrualBefore.TotalExpense);
        Assert.Equal(800_000m, accrualBefore.NetProfit);

        // --- Settle the first installment. ---
        AgencyContext.Current = fixture.AgencyAId;
        var firstInstallmentId = await seedContext.Installments
            .Where(i => i.PolicyId == policy.PolicyId && i.SeqNo == 1)
            .Select(i => i.Id)
            .FirstAsync();
        var paymentResponse = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            firstInstallmentId, 4_000_000m, today, "Cash", null));
        paymentResponse.EnsureSuccessStatusCode();

        // --- Snapshot 2, accrual: full commission income was already recognised at issuance;
        // expense grows by the now-payable first-installment slice. ---
        var accrualAfter = await client.GetFromJsonAsync<PnlResultDto>(
            $"/api/reports/pnl?from={Iso(today)}&to={Iso(today)}&basis=Accrual");
        Assert.Equal(1_000_000m, accrualAfter!.TotalIncome);
        Assert.Equal(600_000m, accrualAfter.TotalExpense);
        Assert.Equal(400_000m, accrualAfter.NetProfit);

        // --- Same snapshot, cash basis: income recognises the fraction of TotalReceivable actually
        // collected. The down payment is its own settled Payment now (PoliciesController.
        // GenerateScheduleAsync), recognised on the policy's IssueDate — 2,000,000 / 10,000,000 =
        // 20% of the 1,000,000 commission = 200,000 — plus the first installment's
        // 4,000,000 / 10,000,000 = 40% = 400,000. Both land on `today` here, so cash income is
        // 600,000 total; expense is the same 600,000 as accrual, so the two bases coincide exactly
        // in this snapshot (they'd diverge if the down payment or installment fell outside the
        // query window).
        var cashAfter = await client.GetFromJsonAsync<PnlResultDto>(
            $"/api/reports/pnl?from={Iso(today)}&to={Iso(today)}&basis=Cash");
        Assert.Equal(600_000m, cashAfter!.AgencyCommissionIncome);
        Assert.Equal(600_000m, cashAfter.TotalIncome);
        Assert.Equal(600_000m, cashAfter.TotalExpense);
        Assert.Equal(0m, cashAfter.NetProfit);

        // --- Breakdown by marketer sums back to the same total. ---
        Assert.Equal(accrualAfter.NetProfit, accrualAfter.ByMarketer.Sum(r => r.NetProfit));
        Assert.Contains(accrualAfter.ByMarketer, r => r.GroupLabel == "بازاریاب سود و زیان");
    }
}
