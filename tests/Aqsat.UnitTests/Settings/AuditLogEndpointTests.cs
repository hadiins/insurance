using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Settings;

/// <summary>لاگ فعالیت — Policy already implements IAuditableEntity, so creating one writes an
/// AuditEntry automatically (CLAUDE.md rule 29); this only proves the read side surfaces it.</summary>
[Collection("WebApplicationFactory")]
public class AuditLogEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuditLogEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task A_policy_creation_shows_up_in_the_audit_log_and_search_filters_it()
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
        var policyNumber = $"POL-AUDIT-{uniqueTag}";

        var policyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            policyNumber, lineId, null, $"مشتری لاگ {uniqueTag}", null, null,
            Vehicle: new VehicleInput("۷۷ه۸۸۸", null, null, null, null, null), Property: null,
            today, today, today.AddYears(1), 8_000_000m, 0m, null, null, false));
        policyResponse.EnsureSuccessStatusCode();

        var log = await client.GetFromJsonAsync<List<AuditLogRowDto>>($"/api/settings/audit-log?search={Uri.EscapeDataString(policyNumber)}");
        Assert.Single(log!);
        Assert.Equal("Created", log![0].Action);
        Assert.Contains(policyNumber, log[0].Description);
    }
}
