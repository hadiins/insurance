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

namespace Aqsat.UnitTests.Reports;

/// <summary>
/// Stage 7/7 — recording an expense shows up in its own report grouped by category, and reduces
/// P&amp;L net profit as a real third expense line (never a discount, never derived).
/// </summary>
[Collection("WebApplicationFactory")]
public class ExpensesEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ExpensesEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Recording_an_expense_appears_in_its_report_and_reduces_pnl_net_profit()
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

        var categoryResponse = await client.PostAsJsonAsync(
            "/api/settings/expense-categories", new CreateExpenseCategoryRequest("اجاره دفتر"));
        categoryResponse.EnsureSuccessStatusCode();
        var category = await categoryResponse.Content.ReadFromJsonAsync<ExpenseCategoryDto>();

        AgencyContext.Current = fixture.AgencyAId;
        var cashBox = new CashBox { AgencyId = fixture.AgencyAId, Name = "صندوق هزینه", IsActive = true };
        seedContext.CashBoxes.Add(cashBox);
        await seedContext.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Before the P&L is asserted, snapshot accrual income so this test does not depend on
        // whatever other policies exist for the agency.
        var pnlBefore = await client.GetFromJsonAsync<PnlResultDto>(
            $"/api/reports/pnl?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}&basis=Accrual");

        var expenseResponse = await client.PostAsJsonAsync("/api/expenses", new CreateExpenseRequest(
            "اجارهٔ ماهانهٔ دفتر", 5_000_000m, today, category!.Id, PaymentMethod.Cash, cashBox.Id, null));
        expenseResponse.EnsureSuccessStatusCode();
        var expense = await expenseResponse.Content.ReadFromJsonAsync<ExpenseDto>();
        Assert.Equal("اجاره دفتر", expense!.CategoryName);
        Assert.Equal("صندوق هزینه", expense.CashBoxName);

        var report = await client.GetFromJsonAsync<ExpensesReportDto>(
            $"/api/expenses?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        Assert.NotNull(report);
        Assert.Equal(5_000_000m, report!.TotalAmount);
        var byCategory = Assert.Single(report.ByCategory, c => c.CategoryName == "اجاره دفتر");
        Assert.Equal(5_000_000m, byCategory.Amount);

        var pnlAfter = await client.GetFromJsonAsync<PnlResultDto>(
            $"/api/reports/pnl?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}&basis=Accrual");
        Assert.NotNull(pnlAfter);
        Assert.Equal(5_000_000m, pnlAfter!.OperatingExpense - pnlBefore!.OperatingExpense);
        Assert.Equal(pnlBefore.NetProfit - 5_000_000m, pnlAfter.NetProfit);
    }
}
