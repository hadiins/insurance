using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Aqsat.UnitTests.InternalOps;

/// <summary>
/// Covers the temporary InternalOpsController used for the 2026-10-06 production wipe: secret
/// gating, the confirm-guard, and — the part that must never regress — that a wipe removes the
/// target agency completely (org, settings, AgencyId-scoped rows, org-only users) while other
/// orgs, shared users, and zero-membership users (the platform owner's shape) survive, and that
/// dbo.AgencyAccessPolicy is left ENABLED. Like the controller, this test is deleted right after
/// the wipe runs.
/// </summary>
[Collection("WebApplicationFactory")]
public class InternalOpsWipeEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestSecret = "test-internal-ops-secret";
    private readonly WebApplicationFactory<Program> _factory;

    public InternalOpsWipeEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Platform:BootstrapSecret"] = TestSecret,
                }));
        });
    }

    [Fact]
    public async Task Wipe_and_report_require_the_ops_secret_and_a_matching_confirm()
    {
        var client = _factory.CreateClient();

        var anonymous = await client.GetAsync("/api/internal-ops/agency-wipe?code=000000");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var wrong = new HttpRequestMessage(HttpMethod.Get, "/api/internal-ops/agency-wipe?code=000000");
        wrong.Headers.Add("X-Ops-Secret", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(wrong)).StatusCode);

        client.DefaultRequestHeaders.Add("X-Ops-Secret", TestSecret);
        var badConfirm = await client.PostAsJsonAsync("/api/internal-ops/agency-wipe",
            new { code = "000000", confirm = "111111" });
        Assert.Equal(HttpStatusCode.BadRequest, badConfirm.StatusCode);

        var missing = await client.GetAsync("/api/internal-ops/agency-wipe?code=does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
    [Fact]
    public async Task Wipe_removes_the_agency_completely_while_other_orgs_and_shared_users_survive()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var agencyCode = $"8{tag[..5]}";

        var orgA = new Organization
        {
            Level = OrganizationLevel.Agency,
            Code = $"A{tag}",
            Name = "نمایندگی حذفی",
            AgencyCode = agencyCode,
        };
        var orgB = new Organization
        {
            Level = OrganizationLevel.Agency,
            Code = $"B{tag}",
            Name = "نمایندگی سالم",
        };
        var role = new Role { Name = $"OpsRole-{tag}" };
        var userOnlyA = new AppUser { FullName = "کاربر فقط الف", Mobile = $"0911{tag[..7]}", PasswordHash = "x" };
        var userBoth = new AppUser { FullName = "کاربر مشترک", Mobile = $"0912{tag[..7]}", PasswordHash = "x" };
        var userOrphan = new AppUser { FullName = "کاربر بدون عضویت", Mobile = $"0913{tag[..7]}", PasswordHash = "x" };
        var userB = new AppUser { FullName = "کاربر بی‌", Mobile = $"0914{tag[..7]}", PasswordHash = "x" };
        Customer customer = default!;

        await using (var setup = TestDbContextFactory.Create())
        {
            setup.AddRange(orgA, orgB, role, userOnlyA, userBoth, userOrphan, userB);
            setup.AddRange(
                new UserOrgRole { User = userOnlyA, Organization = orgA, Role = role },
                new UserOrgRole { User = userBoth, Organization = orgA, Role = role },
                new UserOrgRole { User = userBoth, Organization = orgB, Role = role },
                new UserOrgRole { User = userB, Organization = orgB, Role = role });
            await setup.SaveChangesAsync();

            // AgencyId-scoped marker rows for orgA — the wipe's dynamic DELETE must catch these.
            // The Customers insert runs under an AgencyContext scope for orgA: its AFTER INSERT block
            // predicate compares against SESSION_CONTEXT (see DevSeeder), so an unstamped insert is
            // rejected by the database rather than saved.
            using (AgencyContext.BeginScope(orgA.Id))
            {
                customer = new Customer
                {
                    AgencyId = orgA.Id,
                    ExternalCode = $"EXT-{tag}",
                    FullName = "مشتری حذفی",
                };
                setup.AddRange(new OrgSettings { OrganizationId = orgA.Id }, customer);
                await setup.SaveChangesAsync();
            }
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Ops-Secret", TestSecret);

        // Pre-flight report: found, with a non-trivial row total (org settings + customer +
        // the two orgA memberships at minimum).
        var report = await client.GetAsync($"/api/internal-ops/agency-wipe?code={agencyCode}");
        var reportBody = await report.Content.ReadAsStringAsync();
        Assert.True(report.IsSuccessStatusCode, reportBody);
        using var reportJson = JsonDocument.Parse(reportBody);
        Assert.True(reportJson.RootElement.GetProperty("found").GetBoolean());
        Assert.True(reportJson.RootElement.GetProperty("totalRows").GetInt32() >= 2);

        var wipe = await client.PostAsJsonAsync("/api/internal-ops/agency-wipe",
            new { code = agencyCode, confirm = agencyCode });
        var wipeBody = await wipe.Content.ReadAsStringAsync();
        Assert.True(wipe.IsSuccessStatusCode, wipeBody);
        using var wipeJson = JsonDocument.Parse(wipeBody);
        Assert.True(wipeJson.RootElement.GetProperty("rlsEnabled").GetBoolean());

        // The agency is gone — the same code now reports nothing.
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/internal-ops/agency-wipe?code={agencyCode}")).StatusCode);

        await using var verify = TestDbContextFactory.Create();
        Assert.False(await verify.Organizations.AnyAsync(o => o.Id == orgA.Id));
        Assert.False(await verify.OrgSettings.AnyAsync(s => s.OrganizationId == orgA.Id));
        Assert.False(await verify.Customers.AnyAsync(c => c.Id == customer.Id));
        Assert.False(await verify.UserOrgRoles.AnyAsync(m => m.OrganizationId == orgA.Id));
        Assert.False(await verify.Users.AnyAsync(u => u.Id == userOnlyA.Id));

        // Everything outside the blast radius survives: the other org, its rows, a user who also
        // belongs there, a user with no membership at all (the platform owner's shape), and RLS.
        Assert.True(await verify.Organizations.AnyAsync(o => o.Id == orgB.Id));
        Assert.True(await verify.Users.AnyAsync(u => u.Id == userBoth.Id));
        Assert.True(await verify.Users.AnyAsync(u => u.Id == userOrphan.Id));
        Assert.True(await verify.Users.AnyAsync(u => u.Id == userB.Id));
        Assert.Equal(1, await verify.UserOrgRoles.CountAsync(
            m => m.UserId == userBoth.Id && m.OrganizationId == orgB.Id && !m.IsDeleted));
        Assert.Equal(1, await verify.UserOrgRoles.CountAsync(
            m => m.UserId == userB.Id && m.OrganizationId == orgB.Id && !m.IsDeleted));

        var conn = verify.Database.GetDbConnection();
        await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT is_enabled FROM sys.security_policies WHERE name = 'AgencyAccessPolicy';";
            Assert.True((bool)(await cmd.ExecuteScalarAsync())!);
        }

        await conn.CloseAsync();
    }

}
