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

namespace Aqsat.UnitTests.Platform;

/// <summary>Role is shared across every agency (docs/PHASE-1-SPEC.md §2.2), so only a
/// Platform.Owner session may create/edit/delete one — an agency's own SettingsWrite manager can
/// only ASSIGN an existing role via /api/settings/users, never redefine what it grants.</summary>
[Collection("WebApplicationFactory")]
public class RoleManagementEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RoleManagementEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    private async Task<(HttpClient Client, Guid HqId, Guid SystemRoleId)> CreateOwnerClientAsync()
    {
        await using var context = TestDbContextFactory.Create();
        var hq = new Organization { Level = Domain.Enums.OrganizationLevel.Headquarters, Code = $"HQ-{Guid.NewGuid():N}"[..10], Name = "دفتر مرکزی آزمایشی", IsActive = true };
        context.Organizations.Add(hq);
        await context.SaveChangesAsync();

        var ownerRole = new Role { Name = $"Owner-{Guid.NewGuid():N}"[..12], IsSystemRole = true };
        context.Roles.Add(ownerRole);
        await context.SaveChangesAsync();
        context.RolePermissions.Add(new RolePermission { RoleId = ownerRole.Id, Permission = Permissions.PlatformOwner });
        await context.SaveChangesAsync();

        var hasher = new Aqsat.Infrastructure.Security.PasswordHasher();
        var mobile = $"0914{Guid.NewGuid():N}"[..11];
        var user = new AppUser { FullName = "مالک تستی", Mobile = mobile, PasswordHash = hasher.Hash(DevSeeder.SeededUserPassword), IsActive = true };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        context.UserOrgRoles.Add(new UserOrgRole { UserId = user.Id, OrganizationId = hq.Id, RoleId = ownerRole.Id });
        await context.SaveChangesAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(mobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", hq.Id.ToString());

        return (client, hq.Id, ownerRole.Id);
    }

    [Fact]
    public async Task Create_edit_and_delete_a_role_round_trips_and_the_system_role_is_protected()
    {
        var (client, _, systemRoleId) = await CreateOwnerClientAsync();

        var catalog = await client.GetFromJsonAsync<List<PermissionCatalogItemDto>>("/api/platform/roles/permissions-catalog");
        Assert.DoesNotContain(catalog!, c => c.Key == Permissions.PlatformOwner);

        var uniqueName = $"نقش آزمایشی {Guid.NewGuid():N}"[..20];
        var createResponse = await client.PostAsJsonAsync(
            "/api/platform/roles", new SaveRoleRequest(uniqueName, [Permissions.PolicyRead, Permissions.ReportRead]));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<RoleDto>();
        Assert.Equal(2, created!.Permissions.Count);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/platform/roles/{created.Id}", new SaveRoleRequest(uniqueName, [Permissions.PolicyRead]));
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<RoleDto>();
        Assert.Single(updated!.Permissions);
        Assert.Equal(Permissions.PolicyRead, updated.Permissions[0]);

        var deleteResponse = await client.DeleteAsync($"/api/platform/roles/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var roles = await client.GetFromJsonAsync<List<RoleDto>>("/api/platform/roles");
        Assert.DoesNotContain(roles!, r => r.Id == created.Id);

        var systemRole = roles!.Single(r => r.Id == systemRoleId);
        var editSystemRole = await client.PutAsJsonAsync(
            $"/api/platform/roles/{systemRole.Id}", new SaveRoleRequest(systemRole.Name, []));
        Assert.Equal(HttpStatusCode.BadRequest, editSystemRole.StatusCode);
    }

    [Fact]
    public async Task Trying_to_grant_platform_owner_through_a_role_is_rejected()
    {
        var (client, _, _) = await CreateOwnerClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/platform/roles", new SaveRoleRequest($"نقش نفوذی {Guid.NewGuid():N}"[..20], [Permissions.PlatformOwner]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
