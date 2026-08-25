using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Reports;

/// <summary>
/// Stage 6/7 — «گزارش دریافتی‌ها»: a cash down-payment receipt and a cheque installment payment in
/// the same window must both show up, split correctly by MethodType and cheque status.
/// </summary>
[Collection("WebApplicationFactory")]
public class ReceiptsReportEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ReceiptsReportEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Cash_down_payment_and_cheque_installment_payment_both_appear_split_by_method()
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

        AgencyContext.Current = fixture.AgencyAId;
        var cashBox = new CashBox { AgencyId = fixture.AgencyAId, Name = "صندوق گزارش", IsActive = true };
        seedContext.CashBoxes.Add(cashBox);
        await seedContext.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var policyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-RCPT-{Guid.NewGuid():N}"[..16], salisLineId, null, "مشتری گزارش دریافتی", null, null,
            Vehicle: new VehicleInput("۸۸د۸۸۸", null, null, null, null, null), Property: null,
            today, today, today.AddYears(1), 8_000_000m, 0m, null, null, false));
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        var scheduleResponse = await client.PostAsJsonAsync(
            $"/api/policies/{policy!.PolicyId}/schedule", new ScheduleRequest(2_000_000m, 1));
        scheduleResponse.EnsureSuccessStatusCode();

        var receiveResponse = await client.PostAsJsonAsync(
            $"/api/policies/{policy.PolicyId}/receive-down-payment",
            new ReceiveDownPaymentRequest(today, null, MethodType: Aqsat.Domain.Enums.PaymentMethod.Cash, CashBoxId: cashBox.Id));
        receiveResponse.EnsureSuccessStatusCode();

        var installmentId = await seedContext.Installments
            .Where(i => i.PolicyId == policy.PolicyId).Select(i => i.Id).FirstAsync();
        var chequeResponse = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installmentId, 6_000_000m, today, "چک", null,
            MethodType: Aqsat.Domain.Enums.PaymentMethod.Cheque,
            Cheque: new ChequeDetailsRequest("CHQ-RPT-1", "بانک صادرات", today.AddMonths(1), "زهرا احمدی", cashBox.Id)));
        chequeResponse.EnsureSuccessStatusCode();

        var report = await client.GetFromJsonAsync<ReceiptsReportDto>(
            $"/api/reports/receipts?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");

        Assert.NotNull(report);
        Assert.Equal(2, report!.Count);
        Assert.Equal(8_000_000m, report.TotalAmount);

        var cashRow = Assert.Single(report.ByMethod, m => m.MethodType == "Cash");
        Assert.Equal(2_000_000m, cashRow.Amount);
        var chequeRow = Assert.Single(report.ByMethod, m => m.MethodType == "Cheque");
        Assert.Equal(6_000_000m, chequeRow.Amount);

        var heldRow = Assert.Single(report.ByChequeStatus, c => c.Status == "Held");
        Assert.Equal(6_000_000m, heldRow.Amount);

        Assert.Contains(report.Rows, r => r.MethodType == "Cash" && r.CashBoxName == "صندوق گزارش");
        Assert.Contains(report.Rows, r => r.ChequeNumber == "CHQ-RPT-1" && r.ChequeStatus == "Held");
    }
}
