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

/// <summary>
/// The api.ir half of the platform panel: Platform.Owner only, singleton row created on first read,
/// the stored key never leaves the server unmasked, and a blank ApiKey on PUT keeps the stored one
/// (the panel's ordinary save path). PUT persistence is pinned explicitly — the row must be read
/// tracked, or a save onto an already-existing row silently did nothing while still answering 200.
/// </summary>
[Collection("WebApplicationFactory")]
public class ApiIrSettingsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiIrSettingsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    /// <summary>Same Platform.Owner seeding RoleManagementEndpointTests uses — the apiir panel and
    /// the roles panel share one audience and must stay behind the same gate.</summary>
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
        return client;
    }

    // Shared LocalDB: a leftover singleton row (sibling test, live panel save) would turn "first
    // read creates the row" into a no-op, so every test starts from an empty table.
    private static async Task ClearApiIrSettingsAsync()
    {
        await using var context = TestDbContextFactory.Create();
        await context.Database.ExecuteSqlRawAsync("DELETE FROM ApiIrSettings");
    }

    [Fact]
    public async Task Get_creates_the_singleton_row_on_first_read_and_reports_an_unconfigured_key()
    {
        await ClearApiIrSettingsAsync();

        var client = await CreateOwnerClientAsync();
        var settings = await client.GetFromJsonAsync<ApiIrSettingsDto>("/api/platform/apiir/settings");

        Assert.False(settings!.AllowPaidEndpoints);
        Assert.False(settings.HasApiKey);
        Assert.Null(settings.ApiKeyMasked);
        Assert.Null(settings.UpdatedAt);
    }

    [Fact]
    public async Task Put_persists_even_when_the_row_already_existed_and_never_echoes_the_key()
    {
        await ClearApiIrSettingsAsync();

        var client = await CreateOwnerClientAsync();
        // First read creates the row — every later save lands on an already-existing row, the exact
        // case an AsNoTracking read used to silently drop.
        var first = await client.GetFromJsonAsync<ApiIrSettingsDto>("/api/platform/apiir/settings");
        Assert.False(first!.HasApiKey);

        var saveResponse = await client.PutAsJsonAsync(
            "/api/platform/apiir/settings", new UpdateApiIrSettingsRequest(true, "sk-live-1234567890abcdef"));
        saveResponse.EnsureSuccessStatusCode();
        var saved = await saveResponse.Content.ReadFromJsonAsync<ApiIrSettingsDto>();
        Assert.True(saved!.AllowPaidEndpoints);
        Assert.True(saved.HasApiKey);
        Assert.Equal("sk-l••••cdef", saved.ApiKeyMasked);
        Assert.NotNull(saved.UpdatedAt);

        // The write must survive into a completely different DbContext — not just the response.
        await using var verify = TestDbContextFactory.Create();
        var row = await verify.ApiIrSettings.AsNoTracking().SingleAsync();
        Assert.True(row.AllowPaidEndpoints);
        Assert.Equal("sk-live-1234567890abcdef", row.ApiKey);
        Assert.NotEqual(default, row.UpdatedAt);
    }

    [Fact]
    public async Task Put_with_a_blank_apikey_keeps_the_stored_key()
    {
        await ClearApiIrSettingsAsync();

        var client = await CreateOwnerClientAsync();
        await client.PutAsJsonAsync("/api/platform/apiir/settings", new UpdateApiIrSettingsRequest(false, "sk-live-1234567890abcdef"));
        var second = await client.PutAsJsonAsync("/api/platform/apiir/settings", new UpdateApiIrSettingsRequest(true, "   "));
        second.EnsureSuccessStatusCode();

        var settings = await second.Content.ReadFromJsonAsync<ApiIrSettingsDto>();
        Assert.True(settings!.AllowPaidEndpoints);
        Assert.True(settings.HasApiKey);
        Assert.Equal("sk-l••••cdef", settings.ApiKeyMasked);
    }

    [Fact]
    public async Task A_signed_in_user_without_platform_owner_is_forbidden()
    {
        await ClearApiIrSettingsAsync();

        await using var context = TestDbContextFactory.Create();
        var hq = new Organization { Level = Domain.Enums.OrganizationLevel.Headquarters, Code = $"HQ-{Guid.NewGuid():N}"[..10], Name = "دفتر مرکزی آزمایشی", IsActive = true };
        context.Organizations.Add(hq);
        await context.SaveChangesAsync();

        var staffRole = new Role { Name = $"Staff-{Guid.NewGuid():N}"[..12], IsSystemRole = false };
        context.Roles.Add(staffRole);
        await context.SaveChangesAsync();
        context.RolePermissions.Add(new RolePermission { RoleId = staffRole.Id, Permission = Permissions.PolicyRead });
        await context.SaveChangesAsync();

        var hasher = new Aqsat.Infrastructure.Security.PasswordHasher();
        var mobile = $"0915{Guid.NewGuid():N}"[..11];
        var user = new AppUser { FullName = "کاربر عادی", Mobile = mobile, PasswordHash = hasher.Hash(DevSeeder.SeededUserPassword), IsActive = true };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        context.UserOrgRoles.Add(new UserOrgRole { UserId = user.Id, OrganizationId = hq.Id, RoleId = staffRole.Id });
        await context.SaveChangesAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(mobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", hq.Id.ToString());

        var response = await client.GetAsync("/api/platform/apiir/settings");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
