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

namespace Aqsat.UnitTests.Policies;

/// <summary>
/// Task 6's own check (docs/TASKS.md): create a fire (آتش‌سوزی) policy with no vehicle — accepted.
/// Create a ثالث with no vehicle — rejected with a clear Persian message. RequiresVehicle/
/// RequiresProperty is enforced at the service layer, not just the frontend form.
/// </summary>
[Collection("WebApplicationFactory")]
public class PolicyIssuanceEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PolicyIssuanceEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Third_party_policy_without_a_vehicle_is_rejected_with_a_clear_persian_message()
    {
        var (client, salisLineId, _) = await SeedAsync();

        var response = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-{Guid.NewGuid():N}"[..16], salisLineId, null, "مشتری آزمایشی", null, null,
            Vehicle: null, Property: null,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1),
            9_000_000m, 500_000m, null, null, false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Contains("خودرو", problem!.Title);
    }

    [Fact]
    public async Task Fire_policy_without_a_vehicle_is_accepted_when_property_details_are_given()
    {
        var (client, _, atashLineId) = await SeedAsync();

        var response = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-{Guid.NewGuid():N}"[..16], atashLineId, null, "مشتری آتش‌سوزی", "09120000001", null,
            Vehicle: null,
            Property: new PropertySubjectInput("تهران، خیابان آزادی", "1234567890", "مسکونی", 2_000_000_000m),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1),
            50_000_000m, 1_000_000m, null, null, false));

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreatePolicyResultDto>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result!.PolicyId);
    }

    [Fact]
    public async Task Third_party_policy_with_a_vehicle_is_accepted_and_totals_are_computed_correctly()
    {
        var (client, salisLineId, _) = await SeedAsync();

        var response = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-{Guid.NewGuid():N}"[..16], salisLineId, null, "مشتری ثالث", null, null,
            Vehicle: new VehicleInput("۱۱الف۱۱۱", null, null, null, null, null),
            Property: null,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1),
            9_000_000m, 500_000m, null, null, false));

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = (await client.GetFromJsonAsync<MeResponse>("/api/auth/me"))!.ActiveOrganizationId;
        var policy = await verify.Policies.AsNoTracking().SingleAsync(p => p.Id == result!.PolicyId);
        Assert.Equal(9_500_000m, policy.TotalReceivable);
        Assert.NotNull(policy.VehicleId);
    }

    /// <summary>docs/TASK-25-IDENTITY-VEHICLE.md §5.3 — the structured plate component's four
    /// parts, composed server-side into PlateNormalized, digits normalized to Latin.</summary>
    [Fact]
    public async Task Structured_plate_parts_are_composed_into_plate_normalized()
    {
        var (client, salisLineId, _) = await SeedAsync();

        var response = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"POL-{Guid.NewGuid():N}"[..16], salisLineId, null, "مشتری پلاک", null, null,
            Vehicle: new VehicleInput(
                null, null, null, null, null, null,
                PlateType: 1, PlateTwoDigit: "۵۵", PlateLetter: "الف", PlateThreeDigit: "۵۵۵", PlateIranCode: "۶۳"),
            Property: null,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1),
            9_000_000m, 500_000m, null, null, false));

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreatePolicyResultDto>();

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = (await client.GetFromJsonAsync<MeResponse>("/api/auth/me"))!.ActiveOrganizationId;
        var policy = await verify.Policies.AsNoTracking().Include(p => p.Vehicle).SingleAsync(p => p.Id == result!.PolicyId);
        Assert.Equal("55الف555-63", policy.Vehicle!.PlateNormalized);
        Assert.Equal("55", policy.Vehicle.PlateTwoDigit);
        Assert.Equal("555", policy.Vehicle.PlateThreeDigit);
        Assert.Equal("63", policy.Vehicle.PlateIranCode);
    }

    private async Task<(HttpClient Client, Guid SalisLineId, Guid AtashLineId)> SeedAsync()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await InsuranceLineSeeder.EnsureSeededAsync(seedContext);

        var salisLineId = await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();
        var atashLineId = await seedContext.InsuranceLines
            .Where(l => l.Code == "ATASH").Select(l => l.Id).FirstAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        return (client, salisLineId, atashLineId);
    }

    private sealed record ProblemDetailsDto(string Title);
}
