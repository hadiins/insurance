using System.Net;
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

namespace Aqsat.UnitTests.Marketers;

/// <summary>POST/DELETE /api/marketers/{id}/panel-access — the one-step flow that turns a marketer
/// profile into a working panel login (seeded "بازاریاب" role + Marketer.AppUserId link), plus the
/// issuance-side MarketerId wiring that locks the commission rate at policy creation.</summary>
[Collection("WebApplicationFactory")]
public class MarketerPanelAccessEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly DevSeeder.SeededAuthFixture _fixture;

    public MarketerPanelAccessEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var seedContext = TestDbContextFactory.Create();
        _fixture = DevSeeder.SeedAuthFixtureAsync(seedContext).GetAwaiter().GetResult();
        InsuranceLineSeeder.EnsureSeededAsync(seedContext).GetAwaiter().GetResult();
        MarketerRoleSeeder.EnsureSeededAsync(seedContext).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> LoginAsync(string mobile, string password, Guid orgId)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(mobile, password));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", orgId.ToString());
        return client;
    }

    [Fact]
    public async Task Panel_access_creates_login_resolves_panel_and_shows_only_own_customers()
    {
        var client = await LoginAsync(_fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword, _fixture.AgencyAId);
        var lineId = await LineIdAsync();

        var marketerResponse = await client.PostAsJsonAsync("/api/marketers",
            new CreateMarketerRequest("بازاریاب پنل‌دار", "09120000001", null, "Independent", null));
        marketerResponse.EnsureSuccessStatusCode();
        var marketer = await marketerResponse.Content.ReadFromJsonAsync<MarketerDto>();

        // The rate must exist BEFORE issuance — PoliciesController locks it into the policy row.
        await client.PostAsJsonAsync($"/api/marketers/{marketer!.Id}/rates",
            new SetMarketerRateRequest(lineId, 10m, DateOnly.FromDateTime(DateTime.UtcNow)));

        // One policy introduced by this marketer, one by nobody — the panel must see only the first.
        var policy = await IssuePolicyAsync(client, "مشتری بازاریاب", marketer.Id);
        await IssuePolicyAsync(client, "مشتری بدون بازاریاب", null);
        await client.PostAsJsonAsync($"/api/policies/{policy.PolicyId}/schedule", new ScheduleRequest(0m, 3));

        var panelMobile = $"091{Random.Shared.Next(10_000_000, 99_999_999)}";
        var grant = await client.PostAsJsonAsync($"/api/marketers/{marketer.Id}/panel-access",
            new CreateMarketerPanelAccessRequest(panelMobile, "PanelPass123", null));
        grant.EnsureSuccessStatusCode();
        var granted = await grant.Content.ReadFromJsonAsync<MarketerDto>();
        Assert.NotNull(granted!.AppUserId);
        Assert.Equal(panelMobile, granted.AppUserMobile);

        // The marketer logs in like anyone else and lands on their own panel data.
        var panelClient = await LoginAsync(panelMobile, "PanelPass123", _fixture.AgencyAId);
        var me = await panelClient.GetFromJsonAsync<MarketerDto>("/api/marketer-panel/me");
        Assert.Equal("بازاریاب پنل‌دار", me!.FullName);

        var customers = await panelClient.GetFromJsonAsync<List<MarketerCustomerDto>>("/api/marketer-panel/customers");
        var customer = Assert.Single(customers!);
        Assert.Equal("مشتری بازاریاب", customer.FullName);

        // Every marketer view is audited — same transaction, per CLAUDE.md rule 29's override — and
        // the rate really was locked into the policy row at issuance, not read live later.
        await using var seedContext = TestDbContextFactory.Create();
        AgencyContext.Current = _fixture.AgencyAId;
        var audited = await seedContext.AuditEntries.AsNoTracking()
            .AnyAsync(a => a.PolicyId == policy.PolicyId && a.Description.Contains("بازاریاب پنل‌دار"));
        Assert.True(audited);
        var issued = await seedContext.Policies.AsNoTracking().SingleAsync(p => p.Id == policy.PolicyId);
        Assert.Equal(10m, issued.MarketerRatePercent);

        // The rate locked at issuance drives per-installment slices: 10% of 9,000,000 net premium.
        var commissions = await panelClient.GetFromJsonAsync<CommissionSummaryDto>("/api/marketer-panel/commissions");
        Assert.Equal(900_000m, commissions!.Pending);
        Assert.Equal(0m, commissions.Payable);
        Assert.All(commissions.Entries, e => Assert.Equal("Pending", e.Status));

        // The panel login has exactly one permission — agency-side management stays closed to it.
        var forbidden = await panelClient.GetAsync("/api/marketers");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Panel_access_validates_double_link_and_short_password()
    {
        var client = await LoginAsync(_fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword, _fixture.AgencyAId);

        var first = await client.PostAsJsonAsync("/api/marketers",
            new CreateMarketerRequest("بازاریاب یک", "09120000011", null, "Independent", null));
        first.EnsureSuccessStatusCode();
        var firstMarketer = await first.Content.ReadFromJsonAsync<MarketerDto>();

        var second = await client.PostAsJsonAsync("/api/marketers",
            new CreateMarketerRequest("بازاریاب دو", "09120000012", null, "Independent", null));
        second.EnsureSuccessStatusCode();
        var secondMarketer = await second.Content.ReadFromJsonAsync<MarketerDto>();

        var panelMobile = $"091{Random.Shared.Next(10_000_000, 99_999_999)}";
        var grant = await client.PostAsJsonAsync($"/api/marketers/{firstMarketer!.Id}/panel-access",
            new CreateMarketerPanelAccessRequest(panelMobile, "PanelPass123", null));
        grant.EnsureSuccessStatusCode();

        // Already linked → the panel-access endpoint refuses, and so does linking that user again
        // under a second marketer.
        var twice = await client.PostAsJsonAsync($"/api/marketers/{firstMarketer.Id}/panel-access",
            new CreateMarketerPanelAccessRequest($"091{Random.Shared.Next(10_000_000, 99_999_999)}", "PanelPass123", null));
        Assert.Equal(HttpStatusCode.BadRequest, twice.StatusCode);

        var otherMarketer = await client.PostAsJsonAsync($"/api/marketers/{secondMarketer!.Id}/panel-access",
            new CreateMarketerPanelAccessRequest(panelMobile, "PanelPass123", null));
        Assert.Equal(HttpStatusCode.BadRequest, otherMarketer.StatusCode);

        var shortPassword = await client.PostAsJsonAsync($"/api/marketers/{secondMarketer.Id}/panel-access",
            new CreateMarketerPanelAccessRequest($"091{Random.Shared.Next(10_000_000, 99_999_999)}", "short", null));
        Assert.Equal(HttpStatusCode.BadRequest, shortPassword.StatusCode);
    }

    [Fact]
    public async Task Revoke_unlinks_the_panel_but_keeps_other_role_memberships()
    {
        var client = await LoginAsync(_fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword, _fixture.AgencyAId);

        // An existing staff user (the dual-agency manager) gets panel access on top of Manager —
        // revoking it must only remove the marketer-role membership, never the Manager one.
        var marketerResponse = await client.PostAsJsonAsync("/api/marketers",
            new CreateMarketerRequest("بازاریاب مدیر", "09120000021", null, "Independent", null));
        marketerResponse.EnsureSuccessStatusCode();
        var marketer = await marketerResponse.Content.ReadFromJsonAsync<MarketerDto>();

        var grant = await client.PostAsJsonAsync($"/api/marketers/{marketer!.Id}/panel-access",
            new CreateMarketerPanelAccessRequest(_fixture.DualAgencyManagerMobile, "ignored-existing-user", null));
        grant.EnsureSuccessStatusCode();

        var me = await client.GetFromJsonAsync<MarketerDto>("/api/marketer-panel/me");
        Assert.Equal("بازاریاب مدیر", me!.FullName);

        var revoke = await client.DeleteAsync($"/api/marketers/{marketer.Id}/panel-access");
        revoke.EnsureSuccessStatusCode();

        var afterRevoke = await client.GetAsync("/api/marketer-panel/me");
        Assert.Equal(HttpStatusCode.Forbidden, afterRevoke.StatusCode);

        // The Manager membership in agency A survived — agency-side access still works.
        var stillManager = await client.GetAsync("/api/marketers");
        Assert.Equal(HttpStatusCode.OK, stillManager.StatusCode);

        // A standalone panel user loses login access to the agency entirely on revoke.
        var second = await client.PostAsJsonAsync("/api/marketers",
            new CreateMarketerRequest("بازاریاب تنها", "09120000022", null, "Independent", null));
        var secondMarketer = await second.Content.ReadFromJsonAsync<MarketerDto>();
        var panelMobile = $"091{Random.Shared.Next(10_000_000, 99_999_999)}";
        await client.PostAsJsonAsync($"/api/marketers/{secondMarketer!.Id}/panel-access",
            new CreateMarketerPanelAccessRequest(panelMobile, "PanelPass123", null));

        await client.DeleteAsync($"/api/marketers/{secondMarketer.Id}/panel-access");
        var panelClient = await LoginAsync(panelMobile, "PanelPass123", _fixture.AgencyAId);
        var panelMe = await panelClient.GetAsync("/api/marketer-panel/me");
        Assert.Equal(HttpStatusCode.Forbidden, panelMe.StatusCode);
    }

    [Fact]
    public async Task Seeded_marketer_role_is_assignable_through_the_roles_list()
    {
        var client = await LoginAsync(_fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword, _fixture.AgencyAId);

        var roles = await client.GetFromJsonAsync<List<RoleOptionDto>>("/api/settings/roles");
        Assert.NotNull(roles);
        var marketerRole = roles!.Single(r => r.Name == MarketerRoleSeeder.RoleName);

        // The role is exactly Marketer.SelfView — and no role carrying Platform.Owner ever leaks
        // into this list.
        await using var seedContext = TestDbContextFactory.Create();
        var permissions = await seedContext.RolePermissions.AsNoTracking()
            .Where(p => p.RoleId == marketerRole.Id && !p.IsDeleted)
            .Select(p => p.Permission)
            .ToListAsync();
        var permission = Assert.Single(permissions);
        Assert.Equal(Aqsat.Application.Auth.Permissions.MarketerSelfView, permission);

        var ownerRoles = await seedContext.Roles.AsNoTracking()
            .Where(r => !r.IsDeleted && r.RolePermissions.Any(p => !p.IsDeleted
                && p.Permission == Aqsat.Application.Auth.Permissions.PlatformOwner))
            .Select(r => r.Id)
            .ToListAsync();
        Assert.Empty(roles.Where(r => ownerRoles.Contains(r.Id)));
    }

    private async Task<Guid> LineIdAsync()
    {
        await using var seedContext = TestDbContextFactory.Create();
        return await seedContext.InsuranceLines
            .Where(l => l.Code == InsuranceLineSeeder.ThirdPartyCode).Select(l => l.Id).FirstAsync();
    }

    /// <summary>Issues a real policy through POST /api/policies — third-party line, so the vehicle
    /// requirement is exercised with a structured plate, exactly like the wizard sends it.</summary>
    private async Task<CreatePolicyResultDto> IssuePolicyAsync(HttpClient client, string customerName, Guid? marketerId)
    {
        var lineId = await LineIdAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var response = await client.PostAsJsonAsync("/api/policies", new CreatePolicyRequest(
            $"MKT-{Guid.NewGuid():N}"[..20], lineId, null, customerName, null, null,
            new VehicleInput(null, null, null, null, null, null,
                PlateTwoDigit: "12", PlateLetter: "ب", PlateThreeDigit: "345", PlateIranCode: "22"),
            Property: null,
            today, today, today.AddYears(1), 9_000_000m, 0m, marketerId, null, false));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatePolicyResultDto>())!;
    }
}
