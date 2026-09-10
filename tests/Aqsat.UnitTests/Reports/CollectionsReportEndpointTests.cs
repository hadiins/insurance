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

/// <summary>Task 18's collections report: server-side paging and a summary that tells on-time
/// settlement apart from late settlement and still-open installments.</summary>
[Collection("WebApplicationFactory")]
public class CollectionsReportEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CollectionsReportEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Paging_returns_the_right_slice_and_the_summary_tells_on_time_from_late_settlement()
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

        // The write-off check inside the summary compares against the REAL wall clock
        // (TimeProvider.System — no fake clock is wired through HTTP), so installment due dates
        // must be anchored to actual "now", not an arbitrary past/future literal, or an unsettled
        // installment could spuriously land in "written off" instead of "open". The policy starts
        // 3 months back so the schedule's due dates are in the PAST — the "late settlement" leg
        // needs a PaidOn beyond an already-passed deadline, and the payment-date guard (review B5)
        // refuses future PaidOn values. The last installment is still well inside the write-off
        // horizon, so it counts as open.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = today.AddMonths(-3);

        var policyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-COLL-{Guid.NewGuid():N}"[..16], salisLineId, null, "بیمه‌گذار وصولی", null, null,
            Vehicle: new VehicleInput("۸۸ز۸۸۸", null, null, null, null, null), Property: null,
            today, start, start.AddYears(1), 12_000_000m, 0m, null, null, false));
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        var scheduleResponse = await client.PostAsJsonAsync(
            $"/api/policies/{policy!.PolicyId}/schedule", new ScheduleRequest(0m, 3));
        scheduleResponse.EnsureSuccessStatusCode();
        var schedule = await scheduleResponse.Content.ReadFromJsonAsync<ScheduleResultDto>();

        AgencyContext.Current = fixture.AgencyAId;
        var installmentIds = await seedContext.Installments
            .Where(i => i.PolicyId == policy.PolicyId).OrderBy(i => i.SeqNo).Select(i => i.Id).ToListAsync();
        Assert.Equal(3, installmentIds.Count);
        var due = schedule!.Installments.OrderBy(i => i.SeqNo).ToList();

        // Installment 1: settled on the due date itself — on time (deadline is a few days later).
        var pay1 = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installmentIds[0], due[0].Amount, due[0].DueDate, "نقدی", null));
        pay1.EnsureSuccessStatusCode();

        // Installment 2: settled 10 days after its own due date — the standard 3-day settlement
        // deadline makes this late no matter how holidays shift it.
        var pay2 = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installmentIds[1], due[1].Amount, due[1].DueDate.AddDays(10), "نقدی", null));
        pay2.EnsureSuccessStatusCode();

        // Installment 3 stays open/unpaid — its due date is a full month out, so it can never be
        // mistaken for written off.

        var from = due.Min(i => i.DueDate).AddDays(-5);
        var to = due.Max(i => i.DueDate).AddDays(20);

        var page1 = await client.GetFromJsonAsync<CollectionsReportPageDto>(
            $"/api/reports/collections?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&page=1&pageSize=2");
        Assert.Equal(3, page1!.TotalCount);
        Assert.Equal(2, page1.Rows.Count);

        var page2 = await client.GetFromJsonAsync<CollectionsReportPageDto>(
            $"/api/reports/collections?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&page=2&pageSize=2");
        Assert.Single(page2!.Rows);

        var summary = await client.GetFromJsonAsync<CollectionsSummaryDto>(
            $"/api/reports/collections/summary?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        Assert.Equal(3, summary!.TotalCount);
        Assert.Equal(1, summary.SettledOnTimeCount);
        Assert.Equal(1, summary.SettledLateCount);
        Assert.Equal(1, summary.OpenCount);
        Assert.Equal(0, summary.WrittenOffCount);
    }
}
