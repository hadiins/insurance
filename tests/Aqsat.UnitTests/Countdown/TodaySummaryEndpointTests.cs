using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Countdown;

/// <summary>GET /api/countdown/summary — the header bell's three counters in one call: overdue
/// installments, renewal watches due for a reminder, and incomplete customer profiles.</summary>
[Collection("WebApplicationFactory")]
public class TodaySummaryEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TodaySummaryEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task The_summary_counts_overdue_installments_due_renewals_and_incomplete_profiles()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);
        var lineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        var before = await client.GetFromJsonAsync<TodaySummaryDto>("/api/countdown/summary");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var uniqueTag = Guid.NewGuid().ToString("N")[..8];

        // An installment whose settlement deadline has already passed → overdue.
        var policyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-SU-{uniqueTag}", lineId, null, $"مشتری خلاصهٔ امروز {uniqueTag}", null, null,
            Vehicle: new VehicleInput("۹۹ج۱۱۱", null, null, null, null, null), Property: null,
            today.AddMonths(-3), today.AddMonths(-3), today.AddYears(1), 9_000_000m, 0m, null, null, false));
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();
        var schedule = await client.PostAsJsonAsync(
            $"/api/policies/{policy!.PolicyId}/schedule", new ScheduleRequest(0m, 3));
        schedule.EnsureSuccessStatusCode();

        // A prospect whose expiry is inside its notify window → renewal due.
        var watchResponse = await client.PostAsJsonAsync("/api/renewal-watches", new CreateRenewalWatchRequest(
            null, $"سررسید نزدیک {uniqueTag}", "09150000000", lineId, null, today, 2, null));
        watchResponse.EnsureSuccessStatusCode();

        // The policy's customer was created with no mobile and no national ID → incomplete profile.
        var after = await client.GetFromJsonAsync<TodaySummaryDto>("/api/countdown/summary");

        Assert.True(after!.OverdueInstallments > before!.OverdueInstallments);
        Assert.True(after.DueRenewals > before.DueRenewals);
        Assert.True(after.IncompleteProfiles.Total > before.IncompleteProfiles.Total);
        Assert.True(after.IncompleteProfiles.WithoutMobile >= before.IncompleteProfiles.WithoutMobile);
    }
}
