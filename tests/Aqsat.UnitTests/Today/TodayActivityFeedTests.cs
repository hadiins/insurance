using Aqsat.Application.Common;
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

namespace Aqsat.UnitTests.Today;

/// <summary>
/// The Today activity feed's bounced-cheque row. Bouncing a cheque runs the shared reversal, which
/// soft-deletes the Payment that cheque hangs off, and the global soft-delete filter then drops the
/// row through the required Payment join — the feed goes empty in exactly the case it exists to
/// report. The pair of assertions here pins the feed and the headline count to the same event, and
/// pins the agency boundary that dropping the filter must not breach.
/// </summary>
[Collection("WebApplicationFactory")]
public class TodayActivityFeedTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TodayActivityFeedTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task A_bounced_cheque_reaches_the_feed_although_its_payment_was_reversed()
    {
        var (client, fixture, lineId) = await SignInAsync();
        var agencyId = Guid.Parse(client.DefaultRequestHeaders.GetValues("X-Organization-Id").Single());

        // A cash box is required to receive a cheque, and RLS blocks the insert — so the seed
        // context must act as the agency to create it.
        await using var seedContext = TestDbContextFactory.Create();
        AgencyContext.Current = agencyId;
        var cashBox = new CashBox { AgencyId = agencyId, Name = "صندوق تست چک برگشتی" };
        seedContext.CashBoxes.Add(cashBox);
        await seedContext.SaveChangesAsync();

        var today = IranClock.Today();
        var policy = await CreateScheduledPolicyAsync(client, lineId, "BCH", installments: 3);
        var installment = (await InstallmentsOfAsync(client, policy))[0];

        var chequeNumber = $"B{Guid.NewGuid():N}"[..12];
        var recorded = await client.PostAsJsonAsync("/api/payments", new RecordPaymentRequest(
            installment.InstallmentId, 500_000m, today, "چک", null,
            MethodType: PaymentMethod.Cheque,
            Cheque: new ChequeDetailsRequest(chequeNumber, "بانک ملت", today.AddDays(20), "معرف تستی", cashBox.Id)));
        recorded.EnsureSuccessStatusCode();
        var payment = (await recorded.Content.ReadFromJsonAsync<PaymentResultDto>())!;

        var chequeId = await seedContext.PaymentCheques.AsNoTracking()
            .Where(c => c.PaymentId == payment.PaymentId)
            .Select(c => c.Id)
            .SingleAsync();

        var bounce = await client.PutAsJsonAsync(
            $"/api/payment-cheques/{chequeId}/status", new UpdatePaymentChequeStatusRequest("Bounced"));
        bounce.EnsureSuccessStatusCode();

        // The precondition that makes this a regression test rather than a duplicate of the cheque
        // lifecycle test: bouncing reversed the payment, so the join this feed used to take is gone.
        var reversed = await seedContext.Payments.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(p => p.Id == payment.PaymentId);
        Assert.True(reversed.IsDeleted);

        var dashboard = await client.GetFromJsonAsync<TodayDashboardDto>("/api/today");
        var bouncedRow = Assert.Single(dashboard!.Activity
            .Where(a => a.Kind == "cheque-bounced" && a.Title.Contains(chequeNumber))
            .ToList());

        // The customer name comes off the reversed Payment's own navigation — if the join is broken
        // again this is what goes blank while the row itself survives.
        Assert.False(string.IsNullOrWhiteSpace(bouncedRow.Detail));

        // Rule 15's other half: the headline figure and the feed must describe the same event. They
        // disagreed before the fix — the count read 1 while the feed showed nothing.
        Assert.True(dashboard.Figures.ChequesBouncedCount >= 1);

        // Dropping the global filter is a footgun, so the agency boundary is asserted, not assumed:
        // reading this feed with the filter off must still never cross agencies (RLS is DB-side).
        var clientB = await SignInAsAsync(fixture.DualAgencyManagerMobile, fixture.AgencyBId);
        var otherAgency = await clientB.GetFromJsonAsync<TodayDashboardDto>("/api/today");
        Assert.DoesNotContain(otherAgency!.Activity, a => a.Title.Contains(chequeNumber));
    }

    private async Task<(HttpClient Client, DevSeeder.SeededAuthFixture Fixture, Guid LineId)> SignInAsync()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);
        var lineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var client = await SignInAsAsync(fixture.DualAgencyManagerMobile, fixture.AgencyAId);
        return (client, fixture, lineId);
    }

    private async Task<HttpClient> SignInAsAsync(string mobile, Guid agencyId)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(mobile, DevSeeder.SeededUserPassword));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", agencyId.ToString());
        return client;
    }

    private static async Task<CreatePolicyResultDto> CreateScheduledPolicyAsync(
        HttpClient client, Guid lineId, string tag, int installments)
    {
        var today = IranClock.Today();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var response = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-{tag}-{unique}", lineId, null, $"مشتری چک برگشتی {unique}", null, null,
            Vehicle: new VehicleInput($"۵۶د{unique[..3]}", null, null, null, null, null), Property: null,
            today.AddMonths(-3), today.AddMonths(-3), today.AddYears(1), 9_000_000m, 0m, null, null, false));
        response.EnsureSuccessStatusCode();
        var policy = (await response.Content.ReadFromJsonAsync<CreatePolicyResultDto>())!;

        var schedule = await client.PostAsJsonAsync(
            $"/api/policies/{policy.PolicyId}/schedule", new ScheduleRequest(0m, installments));
        schedule.EnsureSuccessStatusCode();
        return policy;
    }

    private static async Task<IReadOnlyList<InstallmentWorklistRowDto>> InstallmentsOfAsync(
        HttpClient client, CreatePolicyResultDto policy)
    {
        var rows = await client.GetFromJsonAsync<List<InstallmentWorklistRowDto>>(
            $"/api/installments?search={policy.PolicyNumber}");
        return rows!.Where(r => r.PolicyId == policy.PolicyId).ToList();
    }
}
