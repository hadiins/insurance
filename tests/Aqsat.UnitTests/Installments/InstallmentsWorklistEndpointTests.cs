using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Installments;

/// <summary>اقساط معوق / تسویه‌های جزئی — no date window, unlike /api/countdown.</summary>
[Collection("WebApplicationFactory")]
public class InstallmentsWorklistEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public InstallmentsWorklistEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task OverdueOnly_excludes_installments_whose_deadline_has_not_passed()
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

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var uniqueTag = Guid.NewGuid().ToString("N")[..8];

        // Issue date far in the past so the first monthly installment's deadline is already gone.
        var policyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-WL-{uniqueTag}", lineId, null, $"مشتری کارتابل {uniqueTag}", null, null,
            Vehicle: new VehicleInput("۵۵د۶۶۶", null, null, null, null, null), Property: null,
            today.AddMonths(-3), today.AddMonths(-3), today.AddYears(1), 9_000_000m, 0m, null, null, false));
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        var scheduleResponse = await client.PostAsJsonAsync(
            $"/api/policies/{policy!.PolicyId}/schedule", new ScheduleRequest(0m, 3));
        scheduleResponse.EnsureSuccessStatusCode();

        var overdueOnly = await client.GetFromJsonAsync<List<InstallmentWorklistRowDto>>("/api/installments?overdueOnly=true");
        Assert.Contains(overdueOnly!, i => i.PolicyId == policy.PolicyId);
        Assert.All(overdueOnly!.Where(i => i.PolicyId == policy.PolicyId), i => Assert.Equal("Overdue", i.Urgency));

        var all = await client.GetFromJsonAsync<List<InstallmentWorklistRowDto>>("/api/installments");
        Assert.Equal(3, all!.Count(i => i.PolicyId == policy.PolicyId));
    }
}
