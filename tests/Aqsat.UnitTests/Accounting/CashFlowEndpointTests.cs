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
/// «موجودی و گردش صندوق و بانک» — the live balance is opening balance plus arithmetic over every
/// flow kind the system records (Payments in; Expenses, InsurerRemittances and CommissionPayouts
/// out; FundTransfers both ways), and paying a marketer's commission now actually moves cash out
/// of a box while the P&amp;L shows the paid amount without double-counting the expense.
/// </summary>
[Collection("WebApplicationFactory")]
public class CashFlowEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CashFlowEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    // Query strings must be built with an explicit invariant format — on a Persian-locale host a
    // culture-driven render produces Jalali digits the DateOnly binder cannot parse back.
    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task Balances_reflect_every_flow_kind_and_movements_list_them()
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

        // Box A carries an opening balance; box B starts empty and only receives a transfer.
        var boxAResponse = await client.PostAsJsonAsync(
            "/api/settings/cash-and-bank/cash-boxes", new CreateCashBoxRequest("صندوق اصلی", 1_000_000m));
        boxAResponse.EnsureSuccessStatusCode();
        var boxA = await boxAResponse.Content.ReadFromJsonAsync<CashBoxDto>();
        Assert.Equal(1_000_000m, boxA!.OpeningBalance);

        var boxBResponse = await client.PostAsJsonAsync(
            "/api/settings/cash-and-bank/cash-boxes", new CreateCashBoxRequest("صندوق انبار", 0m));
        boxBResponse.EnsureSuccessStatusCode();
        var boxB = await boxBResponse.Content.ReadFromJsonAsync<CashBoxDto>();

        // Outflow: an operating expense paid from box A.
        var categoryResponse = await client.PostAsJsonAsync(
            "/api/settings/expense-categories", new CreateExpenseCategoryRequest("اجاره دفتر"));
        categoryResponse.EnsureSuccessStatusCode();
        var category = await categoryResponse.Content.ReadFromJsonAsync<ExpenseCategoryDto>();
        var expenseResponse = await client.PostAsJsonAsync("/api/expenses", new CreateExpenseRequest(
            "اجارهٔ دفتر", 200_000m, today, category!.Id, PaymentMethod.Cash, boxA.Id, null));
        expenseResponse.EnsureSuccessStatusCode();

        // Inflows: down payment + first installment, both collected into box A.
        var policyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-CF-{Guid.NewGuid():N}"[..16], salisLineId, null, "مشتری گردش نقد", null, null,
            Vehicle: new VehicleInput("۱۲ب۳۴۵", null, null, null, null, null),
            Property: null,
            today, today, today.AddYears(1),
            NetPremium: 10_000_000m, ServiceFee: 0m, MarketerId: null, PreviousInsurer: null, IsRenewal: false));
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        var scheduleResponse = await client.PostAsJsonAsync(
            $"/api/policies/{policy!.PolicyId}/schedule", new ScheduleRequest(2_000_000m, 2));
        scheduleResponse.EnsureSuccessStatusCode();

        var downResponse = await client.PostAsJsonAsync(
            $"/api/policies/{policy.PolicyId}/receive-down-payment",
            new ReceiveDownPaymentRequest(today, null, PaymentMethod.Cash, boxA.Id, null));
        downResponse.EnsureSuccessStatusCode();

        AgencyContext.Current = fixture.AgencyAId;
        var firstInstallmentId = await seedContext.Installments
            .Where(i => i.PolicyId == policy.PolicyId && i.SeqNo == 1).Select(i => i.Id).FirstAsync();
        var paymentResponse = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            firstInstallmentId, 4_000_000m, today, "Cash", null,
            MethodType: PaymentMethod.Cash, CashBoxId: boxA.Id));
        paymentResponse.EnsureSuccessStatusCode();

        // Outflow: remitting the settled first installment to the insurer from box A.
        var remittanceResponse = await client.PostAsJsonAsync("/api/insurer-remittances", new CreateInsurerRemittanceRequest(
            today, PaymentMethod.Cash, boxA.Id, null, null,
            [new RemittanceLineRequest(policy.PolicyId, firstInstallmentId, 4_000_000m)]));
        remittanceResponse.EnsureSuccessStatusCode();

        // Both ways: 500,000 from box A to box B.
        var transferResponse = await client.PostAsJsonAsync("/api/cash-flow/transfers", new CreateFundTransferRequest(
            today, 500_000m, boxA.Id, null, boxB!.Id, null, "تقسیم وجه"));
        transferResponse.EnsureSuccessStatusCode();
        var transfer = await transferResponse.Content.ReadFromJsonAsync<FundTransferDto>();
        Assert.Equal("صندوق اصلی", transfer!.FromLabel);
        Assert.Equal("صندوق انبار", transfer.ToLabel);

        // Box A: 1,000,000 + (2,000,000 + 4,000,000) − (200,000 + 4,000,000 + 500,000) = 2,300,000.
        var balances = await client.GetFromJsonAsync<CashFlowBalancesDto>("/api/cash-flow/balances");
        Assert.NotNull(balances);
        var boxARow = Assert.Single(balances!.CashBoxes, b => b.Id == boxA.Id);
        Assert.Equal(6_000_000m, boxARow.TotalIn);
        Assert.Equal(4_700_000m, boxARow.TotalOut);
        Assert.Equal(2_300_000m, boxARow.Balance);

        var boxBRow = Assert.Single(balances.CashBoxes, b => b.Id == boxB.Id);
        Assert.Equal(500_000m, boxBRow.Balance);

        // The movement view lists every flow kind feeding that balance.
        var movements = await client.GetFromJsonAsync<FundMovementsDto>(
            $"/api/cash-flow/movements?cashBoxId={boxA.Id}&from={Iso(today)}&to={Iso(today)}");
        Assert.NotNull(movements);
        Assert.Equal(6_000_000m, movements!.TotalIn);
        Assert.Equal(4_700_000m, movements.TotalOut);
        Assert.Contains(movements.Rows, r => r.Kind == "دریافتی مشتری" && r.AmountIn == 2_000_000m);
        Assert.Contains(movements.Rows, r => r.Kind == "دریافتی مشتری" && r.AmountIn == 4_000_000m);
        Assert.Contains(movements.Rows, r => r.Kind == "هزینه" && r.AmountOut == 200_000m);
        Assert.Contains(movements.Rows, r => r.Kind == "پرداخت به بیمه‌گر" && r.AmountOut == 4_000_000m);
        Assert.Contains(movements.Rows, r => r.Kind == "انتقال وجه" && r.AmountOut == 500_000m);

        // The same transfer shows as an inflow on box B's side.
        var movementsB = await client.GetFromJsonAsync<FundMovementsDto>($"/api/cash-flow/movements?cashBoxId={boxB.Id}");
        Assert.Contains(movementsB!.Rows, r => r.Kind == "انتقال وجه" && r.AmountIn == 500_000m && r.Label == "انتقال از صندوق اصلی");
    }

    [Fact]
    public async Task Transfer_validates_sides_amount_and_self_reference()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        var boxResponse = await client.PostAsJsonAsync(
            "/api/settings/cash-and-bank/cash-boxes", new CreateCashBoxRequest("صندوق اعتبارسنجی", 0m));
        boxResponse.EnsureSuccessStatusCode();
        var box = await boxResponse.Content.ReadFromJsonAsync<CashBoxDto>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Zero/negative amount.
        var zero = await client.PostAsJsonAsync("/api/cash-flow/transfers",
            new CreateFundTransferRequest(today, 0m, box!.Id, null, null, null));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, zero.StatusCode);

        // Both "from" sides at once.
        var bothFrom = await client.PostAsJsonAsync("/api/cash-flow/transfers",
            new CreateFundTransferRequest(today, 1_000m, box.Id, Guid.NewGuid(), null, null));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, bothFrom.StatusCode);

        // No "to" side at all.
        var noTo = await client.PostAsJsonAsync("/api/cash-flow/transfers",
            new CreateFundTransferRequest(today, 1_000m, box.Id, null, null, null));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, noTo.StatusCode);

        // Same box on both sides.
        var self = await client.PostAsJsonAsync("/api/cash-flow/transfers",
            new CreateFundTransferRequest(today, 1_000m, box.Id, null, box.Id, null));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, self.StatusCode);

        // A box from outside the caller's scope is invisible (RLS) and must not pass validation.
        var crossAgency = await client.PostAsJsonAsync("/api/cash-flow/transfers",
            new CreateFundTransferRequest(today, 1_000m, box.Id, null, null, Guid.NewGuid()));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, crossAgency.StatusCode);
    }

    /// <summary>
    /// Paying a marketer's commission batch now writes a CommissionPayout money outflow, so the
    /// balance drops — and the P&amp;L shows «پورسانت پرداخت‌شدهٔ دوره» as its own line without
    /// double-counting the expense (still recognized at EligibleAt).
    /// </summary>
    [Fact]
    public async Task Paying_commissions_moves_cash_and_reports_the_paid_line_without_double_counting()
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

        var boxResponse = await client.PostAsJsonAsync(
            "/api/settings/cash-and-bank/cash-boxes", new CreateCashBoxRequest("صندوق پورسانت", 5_000_000m));
        boxResponse.EnsureSuccessStatusCode();
        var box = await boxResponse.Content.ReadFromJsonAsync<CashBoxDto>();

        var marketerResponse = await client.PostAsJsonAsync(
            "/api/marketers", new CreateMarketerRequest("بازاریاب گردش", "09129991111", null, "Independent", null));
        marketerResponse.EnsureSuccessStatusCode();
        var marketer = await marketerResponse.Content.ReadFromJsonAsync<MarketerDto>();
        await client.PostAsJsonAsync(
            $"/api/marketers/{marketer!.Id}/rates", new SetMarketerRateRequest(salisLineId, 10m, today.AddDays(-30)));

        var policyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-CP-{Guid.NewGuid():N}"[..16], salisLineId, null, "مشتری پورسانت", null, null,
            Vehicle: new VehicleInput("۱۲ب۳۴۵", null, null, null, null, null),
            Property: null,
            today, today, today.AddYears(1),
            NetPremium: 10_000_000m, ServiceFee: 0m, MarketerId: marketer.Id, PreviousInsurer: null, IsRenewal: false));
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        await client.PostAsJsonAsync($"/api/policies/{policy!.PolicyId}/schedule", new ScheduleRequest(2_000_000m, 2));
        await client.PostAsJsonAsync(
            $"/api/policies/{policy.PolicyId}/receive-down-payment",
            new ReceiveDownPaymentRequest(today, null, PaymentMethod.Cash, box!.Id, null));

        // The down-payment slice (10% of 10,000,000 × 20% = 200,000) is Payable immediately.
        var commissions = await client.GetFromJsonAsync<CommissionSummaryDto>($"/api/marketers/{marketer.Id}/commissions");
        var payable = commissions!.Entries.Where(e => e.Status == "Payable").ToList();
        var payableAmount = payable.Sum(e => e.Amount);
        Assert.Equal(200_000m, payableAmount);

        var payResponse = await client.PostAsJsonAsync($"/api/marketers/{marketer.Id}/commissions/pay",
            new PayCommissionsRequest(payable.Select(e => e.Id).ToList(), PaymentMethod.Cash, box.Id, null, null, null));
        payResponse.EnsureSuccessStatusCode();
        var payResult = await payResponse.Content.ReadFromJsonAsync<PayCommissionsResultDto>();
        Assert.Equal(200_000m, payResult!.TotalAmount);

        // The payout left the box (5,000,000 opening + 2,000,000 down payment in − 200,000 out).
        var balances = await client.GetFromJsonAsync<CashFlowBalancesDto>("/api/cash-flow/balances");
        var boxRow = Assert.Single(balances!.CashBoxes, b => b.Id == box.Id);
        Assert.Equal(200_000m, boxRow.TotalOut);
        Assert.Equal(6_800_000m, boxRow.Balance);
        var movements = await client.GetFromJsonAsync<FundMovementsDto>($"/api/cash-flow/movements?cashBoxId={box.Id}");
        Assert.Contains(movements!.Rows, r => r.Kind == "پورسانت بازاریاب" && r.AmountOut == 200_000m && r.Label == "بازاریاب گردش");

        // The P&L reports the paid amount as its own informational line while the expense itself
        // stays recognized at EligibleAt — TotalExpense must not include it twice.
        var pnl = await client.GetFromJsonAsync<PnlResultDto>(
            $"/api/reports/pnl?from={Iso(today)}&to={Iso(today)}&basis=Accrual");
        Assert.Equal(200_000m, pnl!.MarketerCommissionPaid);
        Assert.Equal(200_000m, pnl.MarketerCommissionExpense);
        Assert.Equal(pnl.MarketerCommissionExpense, pnl.TotalExpense - pnl.DefaultWriteOffExpense - pnl.OperatingExpense);
    }
}
