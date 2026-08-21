using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Renewals;

/// <summary>Task 16's own check, HTTP surface: walk-in prospect registration and the
/// convert/lost lifecycle. Conversion links the watch to the new policy, closing the loop.</summary>
[Collection("WebApplicationFactory")]
public class RenewalWatchEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RenewalWatchEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Registering_a_walk_in_prospect_and_converting_it_links_the_new_policy()
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

        var today = new DateOnly(2026, 3, 10);

        var createResponse = await client.PostAsJsonAsync("/api/renewal-watches", new CreateRenewalWatchRequest(
            CustomerId: null, ProspectName: "مشتری واکینگ", ProspectMobile: "09121230000",
            InsuranceLineId: salisLineId, CurrentInsurer: "بیمهٔ رقیب", CurrentExpiryDate: today.AddDays(60),
            NotifyDaysBefore: 2, MarketerId: null));
        createResponse.EnsureSuccessStatusCode();
        var watch = await createResponse.Content.ReadFromJsonAsync<RenewalWatchDto>();
        Assert.NotNull(watch);
        Assert.Equal("Watching", watch!.Status);

        var policyResponse = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-REN-{Guid.NewGuid():N}"[..16], salisLineId, null, "مشتری واکینگ", "09121230000", null,
            Vehicle: new VehicleInput("۲۲ب۲۲۲", null, null, null, null, null),
            Property: null,
            today, today, today.AddYears(1),
            9_000_000m, 500_000m, null, null, false));
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        var convertResponse = await client.PostAsJsonAsync(
            $"/api/renewal-watches/{watch.Id}/convert", new ConvertRenewalWatchRequest(policy!.PolicyId));
        convertResponse.EnsureSuccessStatusCode();
        var converted = await convertResponse.Content.ReadFromJsonAsync<RenewalWatchDto>();
        Assert.Equal("Converted", converted!.Status);
        Assert.Equal(policy.PolicyId, converted.PolicyId);

        // A closed watch cannot be converted or marked lost again.
        var secondConvert = await client.PostAsJsonAsync(
            $"/api/renewal-watches/{watch.Id}/convert", new ConvertRenewalWatchRequest(policy.PolicyId));
        Assert.Equal(HttpStatusCode.BadRequest, secondConvert.StatusCode);
    }

    [Fact]
    public async Task Both_customer_id_and_prospect_fields_together_is_rejected_and_so_is_neither()
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

        var today = new DateOnly(2026, 3, 10);

        var neitherResponse = await client.PostAsJsonAsync("/api/renewal-watches", new CreateRenewalWatchRequest(
            CustomerId: null, ProspectName: null, ProspectMobile: null,
            InsuranceLineId: salisLineId, CurrentInsurer: null, CurrentExpiryDate: today.AddDays(60),
            NotifyDaysBefore: 2, MarketerId: null));
        Assert.Equal(HttpStatusCode.BadRequest, neitherResponse.StatusCode);
    }
}
