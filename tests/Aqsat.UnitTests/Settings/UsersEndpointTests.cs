using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Domain;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Settings;

/// <summary>کاربران و دسترسی‌ها — add a membership to this agency, list it, then remove access
/// without touching the underlying AppUser (it may hold memberships in other agencies too).</summary>
[Collection("WebApplicationFactory")]
public class UsersEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public UsersEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Create_lists_and_deactivate_a_membership()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        var roles = await client.GetFromJsonAsync<List<RoleOptionDto>>("/api/settings/roles");
        Assert.NotEmpty(roles!);

        var mobile = $"091{Random.Shared.Next(10_000_000, 99_999_999)}";
        var createResponse = await client.PostAsJsonAsync(
            "/api/settings/users", new CreateOrgUserRequest("کاربر آزمایشی", mobile, "Passw0rd!1", roles![0].Id));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<OrgUserDto>();

        var list = await client.GetFromJsonAsync<List<OrgUserDto>>("/api/settings/users");
        Assert.Contains(list!, u => u.MembershipId == created!.MembershipId);

        var deactivateResponse = await client.PutAsJsonAsync($"/api/settings/users/{created!.MembershipId}/deactivate", new { });
        deactivateResponse.EnsureSuccessStatusCode();

        var listAfter = await client.GetFromJsonAsync<List<OrgUserDto>>("/api/settings/users");
        Assert.DoesNotContain(listAfter!, u => u.MembershipId == created.MembershipId);
    }

    /// <summary>An agency manager must never be able to hand Platform.Owner to their own staff
    /// through this endpoint — not via the role picker (excluded from the list) and not by calling
    /// the API directly with a system role's id (rejected server-side too).</summary>
    [Fact]
    public async Task System_role_is_excluded_from_the_picker_and_rejected_if_forced()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);

        var systemRole = new Role { Name = $"مالک آزمایشی {Guid.NewGuid():N}"[..20], IsSystemRole = true };
        seedContext.Roles.Add(systemRole);
        await seedContext.SaveChangesAsync();
        seedContext.RolePermissions.Add(new RolePermission { RoleId = systemRole.Id, Permission = Permissions.PlatformOwner });
        await seedContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        var roles = await client.GetFromJsonAsync<List<RoleOptionDto>>("/api/settings/roles");
        Assert.DoesNotContain(roles!, r => r.Id == systemRole.Id);

        var mobile = $"091{Random.Shared.Next(10_000_000, 99_999_999)}";
        var forcedResponse = await client.PostAsJsonAsync(
            "/api/settings/users", new CreateOrgUserRequest("نفوذی", mobile, "Passw0rd!1", systemRole.Id));
        Assert.Equal(HttpStatusCode.BadRequest, forcedResponse.StatusCode);
    }
}
