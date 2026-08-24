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

    /// <summary>docs/TASK-24-POLICY-NUMBER.md §8 — search must accept a serial with or without
    /// leading zeros, a bare year, and a bare line code, not just a substring of the full number.</summary>
    [Fact]
    public async Task Search_accepts_serial_year_and_line_code_shapes()
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

        // 2026-04-01 is Jalali 1405 (§2's year-comes-from-IssueDate rule) — matches the "405" here.
        var issueDate = new DateOnly(2026, 4, 1);
        var created = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            "1110/576210/405/000456", lineId, null, "مشتری جست‌وجو", null, null,
            Vehicle: new VehicleInput("۱۱د۵۵۵", null, null, null, null, null), Property: null,
            issueDate, issueDate, issueDate.AddYears(1), 8_000_000m, 0m, null, null, false));
        created.EnsureSuccessStatusCode();

        var bySerialNoPadding = await client.GetFromJsonAsync<List<PolicyListItemDto>>("/api/policies?search=456");
        Assert.Contains(bySerialNoPadding!, p => p.PolicyNumber == "1110/576210/405/000456");

        var bySerialPadded = await client.GetFromJsonAsync<List<PolicyListItemDto>>("/api/policies?search=000456");
        Assert.Contains(bySerialPadded!, p => p.PolicyNumber == "1110/576210/405/000456");

        var byFourDigitYear = await client.GetFromJsonAsync<List<PolicyListItemDto>>("/api/policies?search=1405");
        Assert.Contains(byFourDigitYear!, p => p.PolicyNumber == "1110/576210/405/000456");

        var byThreeDigitYear = await client.GetFromJsonAsync<List<PolicyListItemDto>>("/api/policies?search=405");
        Assert.Contains(byThreeDigitYear!, p => p.PolicyNumber == "1110/576210/405/000456");

        var byLineCode = await client.GetFromJsonAsync<List<PolicyListItemDto>>("/api/policies?search=1110");
        Assert.Contains(byLineCode!, p => p.PolicyNumber == "1110/576210/405/000456");
    }
}
