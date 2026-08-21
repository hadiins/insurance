using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Collateral;

/// <summary>Task 18's cheque/promissory-note tracking: registration, the status lifecycle, and the
/// upcoming-view filter. The ChequeColor lookup itself is exercised in ApiIrClientTests (it goes
/// through the real IApiIrClient, which no HTTP-pipeline test calls — the established pattern for
/// every api.ir-backed action in this codebase).</summary>
[Collection("WebApplicationFactory")]
public class CollateralEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CollateralEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Registering_a_cheque_without_a_sayad_id_is_rejected_and_a_valid_one_appears_in_the_upcoming_view()
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

        // The "upcoming" filter compares against the REAL wall clock (TimeProvider is not faked
        // through HTTP), so due dates must be anchored to actual "now".
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var policyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-CHQ-{Guid.NewGuid():N}"[..16], salisLineId, null, "بیمه‌گذار چک", null, null,
            Vehicle: new VehicleInput("۷۷و۷۷۷", null, null, null, null, null), Property: null,
            today, today, today.AddYears(1), 9_000_000m, 0m, null, null, false));
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        var missingSayad = await client.PostAsJsonAsync("/api/collateral", new CreateCollateralRequest(
            policy!.PolicyId, "ChequeSayadi", null, "بانک ملی", 3_000_000m, today.AddDays(20)));
        Assert.Equal(HttpStatusCode.BadRequest, missingSayad.StatusCode);

        var createResponse = await client.PostAsJsonAsync("/api/collateral", new CreateCollateralRequest(
            policy.PolicyId, "ChequeSayadi", "IR010203040506070809101112", "بانک ملی", 3_000_000m, today.AddDays(20)));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CollateralDto>();
        Assert.Equal("Held", created!.Status);

        // Outside the upcoming window (only 5 days out requested, cheque is due in 20) — excluded.
        var tooNarrow = await client.GetFromJsonAsync<List<CollateralDto>>("/api/collateral?type=ChequeSayadi&upcomingDays=5");
        Assert.Empty(tooNarrow!);

        var upcoming = await client.GetFromJsonAsync<List<CollateralDto>>("/api/collateral?type=ChequeSayadi&upcomingDays=30");
        Assert.Contains(upcoming!, c => c.Id == created.Id);

        var statusResponse = await client.PutAsJsonAsync(
            $"/api/collateral/{created.Id}/status", new UpdateCollateralStatusRequest("Bounced"));
        statusResponse.EnsureSuccessStatusCode();
        var updated = await statusResponse.Content.ReadFromJsonAsync<CollateralDto>();
        Assert.Equal("Bounced", updated!.Status);

        // Bounced/Cleared cheques never show up in the "upcoming" view — the field is closed.
        var upcomingAfterBounce = await client.GetFromJsonAsync<List<CollateralDto>>("/api/collateral?type=ChequeSayadi&upcomingDays=30");
        Assert.DoesNotContain(upcomingAfterBounce!, c => c.Id == created.Id);
    }
}
