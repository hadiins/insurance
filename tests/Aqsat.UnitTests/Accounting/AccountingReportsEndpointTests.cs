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

namespace Aqsat.UnitTests.Accounting;

/// <summary>
/// «اصلاح سود و زیان»، «بدهی به بیمه‌گران» و «سنی معوقات» — a cancelled policy must vanish from
/// the P&amp;L on both bases (income AND commission expense), the pending-remittance liability
/// rolls up per insurer and drops when remitted, and the aging report buckets open installments
/// by days past due with the remaining Balance, never the original Amount.
/// </summary>
[Collection("WebApplicationFactory")]
public class AccountingReportsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AccountingReportsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    // Query strings must be built with an explicit invariant format — on a Persian-locale host a
    // culture-driven render produces Jalali digits the DateOnly binder cannot parse back.
    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<HttpClient> LoginClientAsync(WebApplicationFactory<Program> factory, DevSeeder.SeededAuthFixture fixture)
    {
        var client = factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());
        return client;
    }

    /// <summary>
    /// Cancelled policy: accrual income 1,000,000 and cash income 200,000 while active; both zero
    /// (and commission expense zero) after the status flips to Cancelled.
    /// </summary>
    [Fact]
    public async Task Cancelled_policy_disappears_from_pnl_on_both_bases()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);
        var salisLineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var client = await LoginClientAsync(_factory, fixture);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var marketerResponse = await client.PostAsJsonAsync(
            "/api/marketers", new CreateMarketerRequest("بازاریاب ابطال", "09129992222", null, "Independent", null));
        marketerResponse.EnsureSuccessStatusCode();
        var marketer = await marketerResponse.Content.ReadFromJsonAsync<MarketerDto>();
        await client.PostAsJsonAsync(
            $"/api/marketers/{marketer!.Id}/rates", new SetMarketerRateRequest(salisLineId, 10m, today.AddDays(-30)));

        var policyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-CX-{Guid.NewGuid():N}"[..16], salisLineId, null, "مشتری ابطال", null, null,
            Vehicle: new VehicleInput("۱۲ب۳۴۵", null, null, null, null, null),
            Property: null,
            today, today, today.AddYears(1),
            NetPremium: 10_000_000m, ServiceFee: 0m, MarketerId: marketer.Id, PreviousInsurer: null, IsRenewal: false,
            AgencyCommissionPercent: 10m));
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        await client.PostAsJsonAsync($"/api/policies/{policy!.PolicyId}/schedule", new ScheduleRequest(2_000_000m, 2));
        await client.PostAsJsonAsync(
            $"/api/policies/{policy.PolicyId}/receive-down-payment", new ReceiveDownPaymentRequest(today, null));

        // While active: accrual recognizes the full 10% commission at issuance; cash recognizes
        // the 20% actually collected (2,000,000 / 10,000,000 of 1,000,000). Both carry the
        // down-payment commission slice of 200,000 as marketer expense.
        var accrualBefore = await client.GetFromJsonAsync<PnlResultDto>(
            $"/api/reports/pnl?from={Iso(today)}&to={Iso(today)}&basis=Accrual");
        Assert.Equal(1_000_000m, accrualBefore!.AgencyCommissionIncome);
        Assert.Equal(200_000m, accrualBefore.MarketerCommissionExpense);
        Assert.Equal(800_000m, accrualBefore.NetProfit);

        var cashBefore = await client.GetFromJsonAsync<PnlResultDto>(
            $"/api/reports/pnl?from={Iso(today)}&to={Iso(today)}&basis=Cash");
        Assert.Equal(200_000m, cashBefore!.AgencyCommissionIncome);

        AgencyContext.Current = fixture.AgencyAId;
        var tracked = await seedContext.Policies.FirstAsync(p => p.Id == policy.PolicyId);
        tracked.Status = PolicyStatus.Cancelled;
        await seedContext.SaveChangesAsync();

        var accrualAfter = await client.GetFromJsonAsync<PnlResultDto>(
            $"/api/reports/pnl?from={Iso(today)}&to={Iso(today)}&basis=Accrual");
        Assert.Equal(0m, accrualAfter!.TotalIncome);
        Assert.Equal(0m, accrualAfter.MarketerCommissionExpense);
        Assert.Equal(0m, accrualAfter.TotalExpense);
        Assert.Equal(0m, accrualAfter.NetProfit);

        var cashAfter = await client.GetFromJsonAsync<PnlResultDto>(
            $"/api/reports/pnl?from={Iso(today)}&to={Iso(today)}&basis=Cash");
        Assert.Equal(0m, cashAfter!.AgencyCommissionIncome);
        Assert.Equal(0m, cashAfter.TotalIncome);
        Assert.Equal(0m, cashAfter.NetProfit);
    }

    /// <summary>
    /// Per-insurer liability: two down payments through two different insurers produce two rows;
    /// remitting one insurer's money removes it from the pending rollup.
    /// </summary>
    [Fact]
    public async Task Insurer_liability_splits_pending_remitance_by_insurer_and_drops_remitted_rows()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);
        var salisLineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var client = await LoginClientAsync(_factory, fixture);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var boxResponse = await client.PostAsJsonAsync(
            "/api/settings/cash-and-bank/cash-boxes", new CreateCashBoxRequest("صندوق بیمه‌گر", 0m));
        boxResponse.EnsureSuccessStatusCode();
        var box = await boxResponse.Content.ReadFromJsonAsync<CashBoxDto>();

        async Task<Guid> IssueAsync(string insurerName)
        {
            var response = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
                $"POL-IN-{Guid.NewGuid():N}"[..16], salisLineId, null, $"مشتری {insurerName}", null, null,
                Vehicle: new VehicleInput("۱۲ب۳۴۵", null, null, null, null, null),
                Property: null,
                today, today, today.AddYears(1),
                NetPremium: 10_000_000m, ServiceFee: 0m, MarketerId: null, PreviousInsurer: null, IsRenewal: false,
                InsurerName: insurerName));
            response.EnsureSuccessStatusCode();
            var policy = await response.Content.ReadFromJsonAsync<CreatePolicyResultDto>();
            await client.PostAsJsonAsync($"/api/policies/{policy!.PolicyId}/schedule", new ScheduleRequest(2_000_000m, 2));
            await client.PostAsJsonAsync(
                $"/api/policies/{policy.PolicyId}/receive-down-payment",
                new ReceiveDownPaymentRequest(today, null, PaymentMethod.Cash, box!.Id, null));
            return policy.PolicyId;
        }

        var policyA = await IssueAsync("بیمه آ");
        var policyB = await IssueAsync("بیمه ب");

        var before = await client.GetFromJsonAsync<List<InsurerLiabilityRow>>("/api/insurer-remittances/by-insurer");
        Assert.NotNull(before);
        Assert.Equal(2, before!.Count);
        var rowA = Assert.Single(before, r => r.InsurerName == "بیمه آ");
        Assert.Equal(1, rowA.PendingCount);
        Assert.Equal(2_000_000m, rowA.PendingAmount);
        Assert.Equal(today, rowA.OldestCollectedOn);
        Assert.Equal(0m, rowA.RemittedTotal);
        var rowB = Assert.Single(before, r => r.InsurerName == "بیمه ب");
        Assert.Equal(2_000_000m, rowB.PendingAmount);

        // Remit insurer A's collected down payment (a bare payment → InstallmentId null on the line).
        var remittanceResponse = await client.PostAsJsonAsync("/api/insurer-remittances", new CreateInsurerRemittanceRequest(
            today, PaymentMethod.Cash, box!.Id, null, null,
            [new RemittanceLineRequest(policyA, null, 2_000_000m)]));
        remittanceResponse.EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<List<InsurerLiabilityRow>>("/api/insurer-remittances/by-insurer");
        Assert.NotNull(after);
        var remaining = Assert.Single(after!, r => r.InsurerName == "بیمه ب");
        Assert.Equal(2_000_000m, remaining.PendingAmount);
        Assert.DoesNotContain(after, r => r.InsurerName == "بیمه آ");
    }

    /// <summary>
    /// Aging buckets: 4 open installments (2,500,000 each) due at +30 / −10 / −45 / −100 days, with
    /// a 500,000 partial payment targeted at the −10 one — so ۱-۳۰ holds the remaining 2,000,000,
    /// not the original 2,500,000. Cancelling the policy empties the report.
    /// </summary>
    [Fact]
    public async Task Aging_report_buckets_balances_by_days_past_due_and_ignores_cancelled_policies()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);
        var salisLineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var client = await LoginClientAsync(_factory, fixture);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var policyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-AG-{Guid.NewGuid():N}"[..16], salisLineId, null, "مشتری سنی معوقات", "09120001111", null,
            Vehicle: new VehicleInput("۱۲ب۳۴۵", null, null, null, null, null),
            Property: null,
            today, today, today.AddYears(1),
            NetPremium: 10_000_000m, ServiceFee: 0m, MarketerId: null, PreviousInsurer: null, IsRenewal: false));
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        await client.PostAsJsonAsync($"/api/policies/{policy!.PolicyId}/schedule", new ScheduleRequest(0m, 4));

        AgencyContext.Current = fixture.AgencyAId;
        var installments = await seedContext.Installments
            .Where(i => i.PolicyId == policy.PolicyId).OrderBy(i => i.SeqNo).ToListAsync();
        Assert.Equal(4, installments.Count);
        installments[0].DueDate = today.AddDays(30);
        installments[1].DueDate = today.AddDays(-10);
        installments[2].DueDate = today.AddDays(-45);
        installments[3].DueDate = today.AddDays(-100);
        await seedContext.SaveChangesAsync();

        // Explicit allocation so the 500,000 lands on the −10-day installment, not the oldest.
        var paymentResponse = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installments[1].Id, 500_000m, today, "Cash", null,
            [new AllocationLineRequest(installments[1].Id, 500_000m)]));
        paymentResponse.EnsureSuccessStatusCode();

        var report = await client.GetFromJsonAsync<AgingReportDto>("/api/reports/aging");
        Assert.NotNull(report);
        var row = Assert.Single(report!.Rows);
        Assert.Equal("مشتری سنی معوقات", row.CustomerFullName);
        Assert.Equal("09120001111", row.CustomerMobile);
        Assert.Equal(4, row.OpenCount);
        Assert.Equal(2_500_000m, row.CurrentAmount);
        Assert.Equal(2_000_000m, row.Overdue1To30);
        Assert.Equal(2_500_000m, row.Overdue31To60);
        Assert.Equal(2_500_000m, row.Overdue60Plus);
        Assert.Equal(9_500_000m, row.TotalOpen);
        Assert.Equal(today.AddDays(-100), row.OldestOverdueDueDate);

        Assert.Equal(row.CurrentAmount, report.TotalCurrentAmount);
        Assert.Equal(row.Overdue1To30, report.TotalOverdue1To30);
        Assert.Equal(row.Overdue31To60, report.TotalOverdue31To60);
        Assert.Equal(row.Overdue60Plus, report.TotalOverdue60Plus);
        Assert.Equal(row.TotalOpen, report.TotalOpen);

        // Buckets always partition the open balance.
        Assert.Equal(row.TotalOpen, row.CurrentAmount + row.Overdue1To30 + row.Overdue31To60 + row.Overdue60Plus);

        var exportResponse = await client.GetAsync("/api/reports/aging/export");
        exportResponse.EnsureSuccessStatusCode();
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            exportResponse.Content.Headers.ContentType?.MediaType);

        // A cancelled policy owes nothing.
        var tracked = await seedContext.Policies.FirstAsync(p => p.Id == policy.PolicyId);
        tracked.Status = PolicyStatus.Cancelled;
        await seedContext.SaveChangesAsync();

        var afterCancel = await client.GetFromJsonAsync<AgingReportDto>("/api/reports/aging");
        Assert.Empty(afterCancel!.Rows);
        Assert.Equal(0m, afterCancel.TotalOpen);
    }
}
