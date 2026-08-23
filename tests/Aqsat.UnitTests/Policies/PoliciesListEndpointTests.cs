using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Policies;

/// <summary>فهرست بیمه‌نامه‌ها / بیمه‌نامه‌های اقساطی / باطل‌شده‌ها — one endpoint, filtered.</summary>
[Collection("WebApplicationFactory")]
public class PoliciesListEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PoliciesListEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task IsInstallment_and_status_filters_narrow_the_list()
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

        var installmentPolicyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-LIST-{uniqueTag}-A", lineId, null, $"مشتری فهرست {uniqueTag}", null, null,
            Vehicle: new VehicleInput("۱۱ب۲۲۲", null, null, null, null, null), Property: null,
            today, today, today.AddYears(1), 10_000_000m, 0m, null, null, false));
        installmentPolicyResponse.EnsureSuccessStatusCode();

        var cancelledPolicyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-LIST-{uniqueTag}-B", lineId, null, $"مشتری فهرست {uniqueTag}", null, null,
            Vehicle: new VehicleInput("۳۳ج۴۴۴", null, null, null, null, null), Property: null,
            today, today, today.AddYears(1), 5_000_000m, 0m, null, null, false));
        cancelledPolicyResponse.EnsureSuccessStatusCode();
        var cancelledPolicy = await cancelledPolicyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        AgencyContext.Current = fixture.AgencyAId;
        var toCancel = await seedContext.Policies.FirstAsync(p => p.Id == cancelledPolicy!.PolicyId);
        toCancel.Status = PolicyStatus.Cancelled;
        await seedContext.SaveChangesAsync();

        var searchResponse = await client.GetFromJsonAsync<List<PolicyListItemDto>>(
            $"/api/policies?search={Uri.EscapeDataString(uniqueTag)}");
        Assert.Equal(2, searchResponse!.Count);

        var cancelledOnlyResponse = await client.GetFromJsonAsync<List<PolicyListItemDto>>(
            $"/api/policies?search={Uri.EscapeDataString(uniqueTag)}&status=Cancelled");
        Assert.Single(cancelledOnlyResponse!);
        Assert.Equal("Cancelled", cancelledOnlyResponse![0].Status);
    }
}
