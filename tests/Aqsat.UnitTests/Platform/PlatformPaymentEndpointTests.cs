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
/// The payment-gateway half of the platform panel (docs/CUSTOMER-PORTAL-SPEC.md §3): Platform.Owner
/// only, singleton row created on first read, the stored merchant ID never leaves the server
/// unmasked, and a blank OwnerMerchantId on PUT keeps the stored one (the panel's ordinary save
/// path). Enabling a real provider (ZarinPal) without any credential — new or already stored — must
/// be refused, because every customer payment would fail at the PSP later. PUT persistence is
/// pinned explicitly — the row must be read tracked, or a save onto an already-existing row
/// silently did nothing while still answering 200.
/// </summary>
[Collection("WebApplicationFactory")]
public class PlatformPaymentEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PlatformPaymentEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    /// <summary>Same Platform.Owner seeding ApiIrSettingsEndpointTests uses — the payment panel and
    /// the apiir panel share one audience and must stay behind the same gate.</summary>
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
        var mobile = $"0913{Guid.NewGuid():N}"[..11];
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
    private static async Task ClearPlatformPaymentSettingsAsync()
    {
        await using var context = TestDbContextFactory.Create();
        await context.Database.ExecuteSqlRawAsync("DELETE FROM PlatformPaymentSettings");
    }

    [Fact]
    public async Task Get_creates_the_singleton_row_on_first_read_and_reports_an_unconfigured_gateway()
    {
        await ClearPlatformPaymentSettingsAsync();

        var client = await CreateOwnerClientAsync();
        var settings = await client.GetFromJsonAsync<PlatformPaymentSettingsDto>("/api/platform/payment/settings");

        // Mock is the default provider on purpose — the full portal flow is built and verified
        // against it before any real PSP credential is involved.
        Assert.Equal("Mock", settings!.Provider);
        Assert.False(settings.Enabled);
        Assert.False(settings.HasOwnerMerchantId);
        Assert.Null(settings.OwnerMerchantIdMasked);
        Assert.Null(settings.UpdatedAt);
    }

    [Fact]
    public async Task Put_persists_even_when_the_row_already_existed_and_never_echoes_the_merchant_id()
    {
        await ClearPlatformPaymentSettingsAsync();

        var client = await CreateOwnerClientAsync();
        // First read creates the row — every later save lands on an already-existing row, the exact
        // case an AsNoTracking read used to silently drop.
        var first = await client.GetFromJsonAsync<PlatformPaymentSettingsDto>("/api/platform/payment/settings");
        Assert.False(first!.HasOwnerMerchantId);

        var saveResponse = await client.PutAsJsonAsync(
            "/api/platform/payment/settings", new UpdatePlatformPaymentSettingsRequest(
                "Mock", true, "own-live-1234567890abcdef", "https://api.example.ir", 25_000m));
        saveResponse.EnsureSuccessStatusCode();
        var saved = await saveResponse.Content.ReadFromJsonAsync<PlatformPaymentSettingsDto>();
        Assert.True(saved!.Enabled);
        Assert.True(saved.HasOwnerMerchantId);
        Assert.Equal("own-••••cdef", saved.OwnerMerchantIdMasked);
        Assert.NotNull(saved.UpdatedAt);
        // The credential is write-only through this API — the response body must never carry it.
        Assert.DoesNotContain("own-live-1234567890abcdef", await saveResponse.Content.ReadAsStringAsync());

        // The write must survive into a completely different DbContext — not just the response.
        await using var verify = TestDbContextFactory.Create();
        var row = await verify.PlatformPaymentSettings.AsNoTracking().SingleAsync();
        Assert.True(row.Enabled);
        Assert.Equal("own-live-1234567890abcdef", row.OwnerMerchantId);
        Assert.Equal("https://api.example.ir", row.CallbackBaseUrl);
        Assert.Equal(Domain.Enums.PaymentProvider.Mock, row.Provider);
        Assert.NotEqual(default, row.UpdatedAt);
    }

    [Fact]
    public async Task Put_with_a_blank_merchant_id_keeps_the_stored_one()
    {
        await ClearPlatformPaymentSettingsAsync();

        var client = await CreateOwnerClientAsync();
        await client.PutAsJsonAsync("/api/platform/payment/settings",
            new UpdatePlatformPaymentSettingsRequest("Mock", false, "own-live-1234567890abcdef", null, 25_000m));
        var second = await client.PutAsJsonAsync("/api/platform/payment/settings",
            new UpdatePlatformPaymentSettingsRequest("Mock", true, "   ", null, 25_000m));
        second.EnsureSuccessStatusCode();

        var settings = await second.Content.ReadFromJsonAsync<PlatformPaymentSettingsDto>();
        Assert.True(settings!.Enabled);
        Assert.True(settings.HasOwnerMerchantId);
        Assert.Equal("own-••••cdef", settings.OwnerMerchantIdMasked);
    }

    [Fact]
    public async Task Enabling_a_real_provider_without_any_merchant_id_is_rejected_but_mock_needs_none()
    {
        await ClearPlatformPaymentSettingsAsync();

        var client = await CreateOwnerClientAsync();

        // ZarinPal with no credential — new or stored — would fail every payment at the PSP.
        var rejected = await client.PutAsJsonAsync("/api/platform/payment/settings",
            new UpdatePlatformPaymentSettingsRequest("ZarinPal", true, null, null, 25_000m));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        // Mock has no credential requirement — that is its whole point in the build order.
        var mockOk = await client.PutAsJsonAsync("/api/platform/payment/settings",
            new UpdatePlatformPaymentSettingsRequest("Mock", true, null, null, 25_000m));
        mockOk.EnsureSuccessStatusCode();

        // Storing the credential unlocks the real provider…
        var withCredential = await client.PutAsJsonAsync("/api/platform/payment/settings",
            new UpdatePlatformPaymentSettingsRequest("ZarinPal", true, "own-live-1234567890abcdef", null, 25_000m));
        withCredential.EnsureSuccessStatusCode();

        // …and once stored, a later blank save keeps it (the panel's ordinary save path).
        var keepStored = await client.PutAsJsonAsync("/api/platform/payment/settings",
            new UpdatePlatformPaymentSettingsRequest("ZarinPal", true, "   ", null, 25_000m));
        keepStored.EnsureSuccessStatusCode();
        var kept = await keepStored.Content.ReadFromJsonAsync<PlatformPaymentSettingsDto>();
        Assert.Equal("own-••••cdef", kept!.OwnerMerchantIdMasked);
    }

    [Fact]
    public async Task An_unknown_provider_or_a_malformed_callback_url_is_rejected()
    {
        await ClearPlatformPaymentSettingsAsync();

        var client = await CreateOwnerClientAsync();

        var badProvider = await client.PutAsJsonAsync("/api/platform/payment/settings",
            new UpdatePlatformPaymentSettingsRequest("PayPal", true, null, null, 25_000m));
        Assert.Equal(HttpStatusCode.BadRequest, badProvider.StatusCode);

        var badCallback = await client.PutAsJsonAsync("/api/platform/payment/settings",
            new UpdatePlatformPaymentSettingsRequest("Mock", true, null, "not a url", 25_000m));
        Assert.Equal(HttpStatusCode.BadRequest, badCallback.StatusCode);
    }

    [Fact]
    public async Task A_signed_in_user_without_platform_owner_is_forbidden()
    {
        await ClearPlatformPaymentSettingsAsync();

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
        var mobile = $"0912{Guid.NewGuid():N}"[..11];
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

        var response = await client.GetAsync("/api/platform/payment/settings");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}