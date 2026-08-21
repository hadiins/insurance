using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Commission;

/// <summary>
/// Task 13's own check (docs/TASKS.md), exercised over the real HTTP pipeline: commission
/// generation at schedule time, activation on settlement, "7 of 9 paid -> exactly 7 payable slices",
/// and a later rate change never touching entries already generated.
/// </summary>
[Collection("WebApplicationFactory")]
public class MarketerCommissionEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MarketerCommissionEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Settling_seven_of_nine_installments_flips_exactly_seven_slices_payable_and_a_later_rate_change_does_not_touch_them()
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

        var marketerResponse = await client.PostAsJsonAsync(
            "/api/marketers", new CreateMarketerRequest("بازاریاب آزمایشی", "09121110000", null, "Independent", null));
        marketerResponse.EnsureSuccessStatusCode();
        var marketer = await marketerResponse.Content.ReadFromJsonAsync<MarketerDto>();

        var issueDate = new DateOnly(2026, 1, 1);
        var rateResponse = await client.PostAsJsonAsync(
            $"/api/marketers/{marketer!.Id}/rates", new SetMarketerRateRequest(salisLineId, 5m, issueDate.AddDays(-30)));
        rateResponse.EnsureSuccessStatusCode();

        var policyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-MKT-{Guid.NewGuid():N}"[..16], salisLineId, null, "مشتری بازاریاب", null, null,
            Vehicle: new VehicleInput("۱۲ب۱۲۳", null, null, null, null, null),
            Property: null,
            issueDate, issueDate, issueDate.AddYears(1),
            10_700_000m, 0m, marketer.Id, null, false));
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        var scheduleResponse = await client.PostAsJsonAsync(
            $"/api/policies/{policy!.PolicyId}/schedule", new ScheduleRequest(1_700_000m, 9));
        scheduleResponse.EnsureSuccessStatusCode();
        var schedule = await scheduleResponse.Content.ReadFromJsonAsync<ScheduleResultDto>();

        // 10 slices total (1 down-payment + 9 installments), summing to 10,700,000 * 5% = 535,000.
        var afterSchedule = await client.GetFromJsonAsync<CommissionSummaryDto>($"/api/marketers/{marketer.Id}/commissions");
        Assert.Equal(10, afterSchedule!.Entries.Count);
        Assert.Equal(535_000m, afterSchedule.Pending + afterSchedule.Payable + afterSchedule.Paid);
        Assert.Equal(1_700_000m * 5m / 100m, afterSchedule.Payable); // down-payment slice, payable immediately
        Assert.Equal(afterSchedule.Entries.Count - 1, afterSchedule.Entries.Count(e => e.Status == "Pending"));

        // Settle exactly the first 7 (oldest-due-first) of the 9 installments.
        AgencyContext.Current = fixture.AgencyAId;
        foreach (var installment in schedule!.Installments.OrderBy(i => i.SeqNo).Take(7))
        {
            var installmentId = await seedContext.Installments
                .Where(i => i.PolicyId == policy.PolicyId && i.SeqNo == installment.SeqNo)
                .Select(i => i.Id)
                .FirstAsync();

            var paymentResponse = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
                installmentId, installment.Amount, issueDate, "Cash", null));
            paymentResponse.EnsureSuccessStatusCode();
        }

        var afterSettlement = await client.GetFromJsonAsync<CommissionSummaryDto>($"/api/marketers/{marketer.Id}/commissions");
        // Down-payment slice (already payable) + 7 newly-settled installment slices = 8 payable; 2 stay pending forever.
        Assert.Equal(8, afterSettlement!.Entries.Count(e => e.Status == "Payable"));
        Assert.Equal(2, afterSettlement.Entries.Count(e => e.Status == "Pending"));

        var ratesBeforeChange = afterSettlement.Entries.ToDictionary(e => e.Id, e => e.Amount);

        // A rate change today must not alter yesterday's entries.
        var newRateResponse = await client.PostAsJsonAsync(
            $"/api/marketers/{marketer.Id}/rates", new SetMarketerRateRequest(salisLineId, 20m, DateOnly.FromDateTime(DateTime.UtcNow)));
        newRateResponse.EnsureSuccessStatusCode();

        var afterRateChange = await client.GetFromJsonAsync<CommissionSummaryDto>($"/api/marketers/{marketer.Id}/commissions");
        foreach (var entry in afterRateChange!.Entries)
        {
            Assert.Equal(ratesBeforeChange[entry.Id], entry.Amount);
        }
    }
}
