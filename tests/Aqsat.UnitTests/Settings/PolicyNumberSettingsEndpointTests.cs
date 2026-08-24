using System.Net;
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

namespace Aqsat.UnitTests.Settings;

/// <summary>docs/TASK-24-POLICY-NUMBER.md §7 — settings ← line-code mapping and the tunable parts
/// of the number format, plus §4.3's service-layer lock on AgencyCode.</summary>
[Collection("WebApplicationFactory")]
public class PolicyNumberSettingsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PolicyNumberSettingsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Line_codes_are_lazily_seeded_with_parsian_defaults()
    {
        var (client, _) = await SeedAsync();

        var codes = await client.GetFromJsonAsync<List<InsuranceLineCodeDto>>("/api/settings/policy-number/line-codes");

        Assert.NotNull(codes);
        Assert.Contains(codes!, c => c.Code == "1110");
    }

    [Fact]
    public async Task A_code_already_used_by_another_line_is_rejected()
    {
        var (client, fixture) = await SeedAsync();

        await using var seedContext = TestDbContextFactory.Create();
        var atashLineId = await seedContext.InsuranceLines.Where(l => l.Code == "ATASH").Select(l => l.Id).FirstAsync();

        var response = await client.PostAsJsonAsync(
            "/api/settings/policy-number/line-codes", new CreateInsuranceLineCodeRequest(atashLineId, "1110"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _ = fixture;
    }

    [Fact]
    public async Task Format_updates_persist_and_reread_reflects_them()
    {
        var (client, _) = await SeedAsync();

        var updateResponse = await client.PutAsJsonAsync(
            "/api/settings/policy-number/format",
            new UpdatePolicyNumberFormatRequest("-", 4, 6, 4, 7, true));
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<PolicyNumberFormatDto>();
        Assert.Equal("-", updated!.Separator);
        Assert.Equal(4, updated.YearDigits);
        Assert.Equal(7, updated.SerialLength);

        var reread = await client.GetFromJsonAsync<PolicyNumberFormatDto>("/api/settings/policy-number/format");
        Assert.Equal("-", reread!.Separator);
        Assert.Equal(7, reread.SerialLength);
    }

    [Fact]
    public async Task Agency_code_locks_after_the_first_policy_exists()
    {
        var (client, fixture) = await SeedAsync();

        var firstSet = await client.PutAsJsonAsync("/api/settings/agency/agency-code", new UpdateAgencyCodeRequest("576210"));
        firstSet.EnsureSuccessStatusCode();
        var afterFirstSet = await firstSet.Content.ReadFromJsonAsync<AgencySettingsDto>();
        Assert.False(afterFirstSet!.AgencyCodeLocked);

        await using var seedContext = TestDbContextFactory.Create();
        var lineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();

        var createPolicy = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-LOCK-{Guid.NewGuid():N}"[..16], lineId, null, "مشتری قفل", null, null,
            Vehicle: new VehicleInput("۹۹و۹۹۹", null, null, null, null, null), Property: null,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1),
            9_000_000m, 0m, null, null, false));
        createPolicy.EnsureSuccessStatusCode();

        var settingsAfterPolicy = await client.GetFromJsonAsync<AgencySettingsDto>("/api/settings/agency");
        Assert.True(settingsAfterPolicy!.AgencyCodeLocked);

        var secondAttempt = await client.PutAsJsonAsync("/api/settings/agency/agency-code", new UpdateAgencyCodeRequest("999999"));
        Assert.Equal(HttpStatusCode.BadRequest, secondAttempt.StatusCode);
        _ = fixture;
    }

    private async Task<(HttpClient Client, DevSeeder.SeededAuthFixture Fixture)> SeedAsync()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());
        return (client, fixture);
    }
}
