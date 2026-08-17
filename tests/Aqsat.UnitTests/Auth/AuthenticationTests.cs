using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Aqsat.UnitTests.Auth;

/// <summary>
/// End-to-end over real HTTP against the real app pipeline (JwtBearer + ScopeResolutionMiddleware +
/// authorization policies) and the same LocalDB database Task 3 migrated — not a mocked auth layer.
/// Proves Task 4's own check: org switching and 403-not-500 on a missing permission.
/// </summary>
[Collection("WebApplicationFactory")]
public class AuthenticationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthenticationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    private async Task<(DevSeeder.SeededAuthFixture Fixture, HttpClient Client)> SeedAndCreateClientAsync()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        return (fixture, _factory.CreateClient());
    }

    private static async Task<string> LoginAsync(HttpClient client, string mobile)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(mobile, DevSeeder.SeededUserPassword));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    [Fact]
    public async Task Login_then_me_lists_both_organizations_for_the_dual_agency_user()
    {
        var (fixture, client) = await SeedAndCreateClientAsync();
        var token = await LoginAsync(client, fixture.DualAgencyManagerMobile);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");

        Assert.NotNull(me);
        Assert.Equal(2, me!.Organizations.Count);
        Assert.Contains(me.Organizations, o => o.OrganizationId == fixture.AgencyAId);
        Assert.Contains(me.Organizations, o => o.OrganizationId == fixture.AgencyBId);
    }

    [Fact]
    public async Task Same_token_resolves_a_different_active_organization_after_switching_the_header()
    {
        var (fixture, client) = await SeedAndCreateClientAsync();
        var token = await LoginAsync(client, fixture.DualAgencyManagerMobile);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        client.DefaultRequestHeaders.Remove("X-Organization-Id");
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());
        var meAsAgencyA = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.Equal(fixture.AgencyAId, meAsAgencyA!.ActiveOrganizationId);

        client.DefaultRequestHeaders.Remove("X-Organization-Id");
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyBId.ToString());
        var meAsAgencyB = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.Equal(fixture.AgencyBId, meAsAgencyB!.ActiveOrganizationId);
    }

    /// <summary>
    /// The dual-agency user holds Manager in Agency A and Staff (no Payment.Write) in Agency B —
    /// this is the actual substance of "two roles in two agencies": switching org must change the
    /// *permission set*, not just which OrganizationId comes back.
    /// </summary>
    [Fact]
    public async Task Switching_organization_changes_the_resolved_permission_set_because_the_role_differs_per_agency()
    {
        var (fixture, client) = await SeedAndCreateClientAsync();
        var token = await LoginAsync(client, fixture.DualAgencyManagerMobile);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());
        var meAsManagerInAgencyA = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.Contains(Permissions.PaymentWrite, meAsManagerInAgencyA!.Permissions);

        var probeAsManager = await client.GetAsync("/api/_diagnostics/payment-write-probe");
        Assert.Equal(HttpStatusCode.OK, probeAsManager.StatusCode);

        client.DefaultRequestHeaders.Remove("X-Organization-Id");
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyBId.ToString());
        var meAsStaffInAgencyB = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.DoesNotContain(Permissions.PaymentWrite, meAsStaffInAgencyB!.Permissions);

        var probeAsStaff = await client.GetAsync("/api/_diagnostics/payment-write-probe");
        Assert.Equal(HttpStatusCode.Forbidden, probeAsStaff.StatusCode);
    }

    [Fact]
    public async Task User_without_PaymentWrite_gets_403_not_500()
    {
        var (fixture, client) = await SeedAndCreateClientAsync();
        var token = await LoginAsync(client, fixture.AgencyOnlyStaffMobile);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        var response = await client.GetAsync("/api/_diagnostics/payment-write-probe");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Requesting_an_organization_the_caller_does_not_belong_to_is_rejected_not_silently_empty()
    {
        var (fixture, client) = await SeedAndCreateClientAsync();
        var token = await LoginAsync(client, fixture.AgencyOnlyStaffMobile);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyBId.ToString());

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task No_token_is_401()
    {
        var (_, client) = await SeedAndCreateClientAsync();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
