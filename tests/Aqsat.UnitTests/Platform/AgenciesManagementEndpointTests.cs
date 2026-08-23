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

/// <summary>Creating an agency without its first user would leave it permanently unreachable —
/// this endpoint creates both atomically, auto-provisioning a default manager role the first time
/// so a fresh install (no roles besides the system owner role) isn't a dead end.</summary>
[Collection("WebApplicationFactory")]
public class AgenciesManagementEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AgenciesManagementEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    private async Task<HttpClient> CreateOwnerClientAsync()
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
        var mobile = $"0916{Guid.NewGuid():N}"[..11];
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
        return client;
    }

    [Fact]
    public async Task Creating_an_agency_also_creates_a_working_first_login_for_it()
    {
        var client = await CreateOwnerClientAsync();
        var uniqueTag = Guid.NewGuid().ToString("N")[..8];
        var managerMobile = $"0917{uniqueTag[..7]}";

        var createResponse = await client.PostAsJsonAsync("/api/platform/agencies", new CreateAgencyRequest(
            $"AG-{uniqueTag}", $"نمایندگی آزمایشی {uniqueTag}", "تهران", "بیمهٔ آزمایشی",
            "مدیر جدید", managerMobile, "Manager-Pass1", null));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreateAgencyResultDto>();
        Assert.Equal("مدیر نمایندگی", created!.RoleName);

        var agencies = await client.GetFromJsonAsync<List<AgencyDto>>("/api/platform/agencies");
        Assert.Contains(agencies!, a => a.Id == created.Agency.Id && a.UserCount == 1);

        // The freshly created manager can actually log in and reach agency-scoped data.
        var managerClient = _factory.CreateClient();
        var managerLogin = await managerClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(managerMobile, "Manager-Pass1"));
        managerLogin.EnsureSuccessStatusCode();
        var managerToken = (await managerLogin.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        managerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);

        var me = await managerClient.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.Equal(created.Agency.Id, me!.ActiveOrganizationId);
        Assert.Contains(Permissions.PolicyRead, me.Permissions);
        Assert.DoesNotContain(Permissions.PlatformOwner, me.Permissions);
    }

    [Fact]
    public async Task Duplicate_agency_code_is_rejected()
    {
        var client = await CreateOwnerClientAsync();
        var uniqueTag = Guid.NewGuid().ToString("N")[..8];
        var code = $"AG-{uniqueTag}";

        var first = await client.PostAsJsonAsync("/api/platform/agencies", new CreateAgencyRequest(
            code, "نمایندگی اول", null, null, "مدیر اول", $"0918{uniqueTag[..7]}", "Manager-Pass1", null));
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync("/api/platform/agencies", new CreateAgencyRequest(
            code, "نمایندگی دوم", null, null, "مدیر دوم", $"0919{uniqueTag[..7]}", "Manager-Pass2", null));
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }
}
