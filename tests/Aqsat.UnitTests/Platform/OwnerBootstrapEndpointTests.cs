using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Aqsat.UnitTests.Platform;

/// <summary>docs/UPDATE-SYSTEM.md rule 1: the owner account belongs to a real Headquarters
/// organization, never any agency — and this endpoint is a true one-time bootstrap: a second call
/// after an owner already exists must be refused, not create a second owner.</summary>
[Collection("WebApplicationFactory")]
public class OwnerBootstrapEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestSecret = "test-owner-secret";
    private readonly WebApplicationFactory<Program> _factory;

    public OwnerBootstrapEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Platform:BootstrapSecret"] = TestSecret });
            });
        });
    }

    [Fact]
    public async Task Wrong_secret_is_rejected_and_correct_secret_creates_a_working_owner_login()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var client = _factory.CreateClient();

        var uniqueTag = Guid.NewGuid().ToString("N")[..8];
        var mobile = $"0912{uniqueTag[..7]}";

        // Order-independent regardless of whether some other test in this shared dev database has
        // already bootstrapped an owner — a wrong secret is always rejected.
        var wrongSecretResponse = await client.PostAsJsonAsync(
            "/api/platform/bootstrap-owner", new BootstrapOwnerRequest("wrong-secret", "مالک آزمایشی", mobile, "Owner-Pass1"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongSecretResponse.StatusCode);

        // The rejected attempt is a security signal, not just a log line — the owner's dashboard
        // feed must carry it (keyed by this test's unique mobile to stay isolated in the shared DB).
        var suspiciousEvent = await seedContext.SecurityEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Mobile == mobile && e.Type == SecurityEventType.SuspiciousActivity);
        Assert.NotNull(suspiciousEvent);
        Assert.Equal(SecuritySeverity.Warning, suspiciousEvent.Severity);

        var statusBefore = await client.GetFromJsonAsync<BootstrapStatusDto>("/api/platform/bootstrap-owner/status");
        if (!statusBefore!.Available)
        {
            // Another test (e.g. RoleManagementEndpointTests) already created an owner-holding
            // membership in this shared database — verify the "only once" guard still holds and stop.
            var blocked = await client.PostAsJsonAsync(
                "/api/platform/bootstrap-owner", new BootstrapOwnerRequest(TestSecret, "مالک آزمایشی", mobile, "Owner-Pass1"));
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            return;
        }

        var response = await client.PostAsJsonAsync(
            "/api/platform/bootstrap-owner", new BootstrapOwnerRequest(TestSecret, "مالک آزمایشی", mobile, "Owner-Pass1"));
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrWhiteSpace(login!.Token));

        // The freshly minted token actually works and carries Platform.Owner.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.Contains("Platform.Owner", me!.Permissions);

        var statusAfter = await client.GetFromJsonAsync<BootstrapStatusDto>("/api/platform/bootstrap-owner/status");
        Assert.False(statusAfter!.Available);

        var secondAttempt = await client.PostAsJsonAsync(
            "/api/platform/bootstrap-owner", new BootstrapOwnerRequest(TestSecret, "مالک دوم", $"0913{uniqueTag[..7]}", "Owner-Pass2"));
        Assert.Equal(HttpStatusCode.Conflict, secondAttempt.StatusCode);
    }

    /// <summary>Regression for the 2026-09-19 production failure: on a genuinely fresh install
    /// (no role holding Platform.Owner yet) the bootstrap must create the role itself. The old
    /// code set the scalar FK `RoleId = ownerRole.Id` on an UNSAVED role whose server-generated
    /// Id (NEWSEQUENTIALID) doesn't exist yet — EF saw no dependency, ordered the RolePermissions
    /// INSERT before Roles, and SQL threw FK_RolePermissions_Roles_RoleId (error 547). The other
    /// test above never exercises this path because some other test in the shared database has
    /// normally already created an owner; this test strips that state first. Test-database-only
    /// physical deletes are safe: the suite wipes its tables on every run anyway.</summary>
    [Fact]
    public async Task Bootstrap_creates_owner_role_itself_on_a_fresh_install()
    {
        await using var setup = TestDbContextFactory.Create();

        // Simulate a fresh install: no membership may hold Platform.Owner.
        var ownerMemberships = await setup.UserOrgRoles
            .Include(m => m.Role).ThenInclude(r => r.RolePermissions)
            .Where(m => !m.IsDeleted && m.Role.RolePermissions.Any(p => p.Permission == Permissions.PlatformOwner && !p.IsDeleted))
            .ToListAsync();
        foreach (var membership in ownerMemberships)
        {
            membership.IsDeleted = true;
            membership.DeletedAt = DateTimeOffset.UtcNow;
        }

        // Free the canonical owner-role name (Roles.Name is UNIQUELY indexed, deleted rows
        // included) so the bootstrap can insert its own role fresh.
        var staleRoles = await setup.Roles
            .Include(r => r.RolePermissions)
            .Where(r => r.Name == "مالک نرم‌افزار")
            .ToListAsync();
        foreach (var role in staleRoles)
        {
            setup.RolePermissions.RemoveRange(role.RolePermissions);
            setup.Roles.Remove(role);
        }

        // Other tests mint their own owner roles (Owner-xxxxxx holding Platform.Owner). The
        // bootstrap reuses ANY system role holding that permission, so every such role must be
        // soft-deleted too — otherwise a shared-database run "fresh install" silently reuses one
        // and the created membership never lands on «مالک نرم‌افزار».
        var otherOwnerRoles = await setup.Roles
            .Where(r => r.Name != "مالک نرم‌افزار"
                && r.RolePermissions.Any(p => p.Permission == Permissions.PlatformOwner && !p.IsDeleted))
            .ToListAsync();
        foreach (var role in otherOwnerRoles)
        {
            role.IsDeleted = true;
            role.DeletedAt = DateTimeOffset.UtcNow;
        }

        await setup.SaveChangesAsync();

        var client = _factory.CreateClient();
        var uniqueTag = Guid.NewGuid().ToString("N")[..8];
        var mobile = $"0914{uniqueTag[..7]}";

        // Before the fix this returned 500 (FK violation on RolePermissions).
        var response = await client.PostAsJsonAsync(
            "/api/platform/bootstrap-owner", new BootstrapOwnerRequest(TestSecret, "مالک نصب تازه", mobile, "Owner-Pass1"));
        response.EnsureSuccessStatusCode();

        await using var verify = TestDbContextFactory.Create();
        var ownerRole = await verify.Roles.AsNoTracking()
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Name == "مالک نرم‌افزار");
        Assert.NotNull(ownerRole);
        Assert.True(ownerRole!.IsSystemRole);
        Assert.Contains(ownerRole.RolePermissions, p => p.Permission == Permissions.PlatformOwner);

        // The minted token carries Platform.Owner — the whole point of the bootstrap.
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrWhiteSpace(login!.Token));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.Contains("Platform.Owner", me!.Permissions);
    }

    private sealed record BootstrapStatusDto(bool Available);
}
