using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Sms;
using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aqsat.UnitTests.Settings;

/// <summary>«منطقهٔ خطر» — clears every policy/installment for the caller's own agency, gated
/// behind typing the agency's own Code back AND a 6-digit SMS code sent to the agency-manager
/// mobile configured in settings (never the logged-in user's own number).</summary>
[Collection("WebApplicationFactory")]
public class ClearAgencyDataEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly RecordingSmsSender _smsSender = new();
    private readonly WebApplicationFactory<Program> _factory;

    public ClearAgencyDataEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services => services.AddSingleton<ISmsSender>(_smsSender));
        });
    }

    [Fact]
    public async Task Without_a_manager_mobile_or_a_valid_otp_the_wipe_is_rejected_and_with_both_it_clears_and_audits()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(seedContext);
        var mobile = await SeedFullManagerAsync(seedContext, agencyA.AgencyId);

        // The OTP must go to the configured agency-manager mobile, not this user's own number.
        var managerMobile = $"0912{Guid.NewGuid():N}"[..11];
        seedContext.OrgSettings.Add(new OrgSettings
        {
            OrganizationId = agencyA.AgencyId,
            DangerZoneManagerMobile = managerMobile,
        });
        await seedContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(mobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", agencyA.AgencyId.ToString());

        var agencySettings = await client.GetFromJsonAsync<AgencySettingsDto>("/api/settings/agency");
        var confirmCode = agencySettings!.Code;

        var policiesBefore = await client.GetFromJsonAsync<List<PolicyListItemDto>>("/api/policies?");
        Assert.NotEmpty(policiesBefore!);

        // No OTP at all — rejected even with the correct confirm code.
        var noOtp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/settings/agency/data")
        {
            Content = JsonContent.Create(new ClearAgencyDataRequest(confirmCode)),
        });
        Assert.Equal(HttpStatusCode.BadRequest, noOtp.StatusCode);

        // Wrong OTP — rejected too, and the code stays consumed-or-not but never authorizes.
        await client.PostAsync("/api/settings/agency/data/request-otp", null);
        Assert.Equal(managerMobile, _smsSender.SentTo[^1]); // manager's mobile, not the caller's
        var wrongOtp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/settings/agency/data")
        {
            Content = JsonContent.Create(new ClearAgencyDataRequest(confirmCode, "000000")),
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrongOtp.StatusCode);

        var policiesStillThere = await client.GetFromJsonAsync<List<PolicyListItemDto>>("/api/policies?");
        Assert.Equal(policiesBefore!.Count, policiesStillThere!.Count);

        // Correct confirm code + the OTP that was actually sent.
        await client.PostAsync("/api/settings/agency/data/request-otp", null);
        var code = _smsSender.LastCode();
        var correct = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/settings/agency/data")
        {
            Content = JsonContent.Create(new ClearAgencyDataRequest(confirmCode, code)),
        });
        Assert.Equal(HttpStatusCode.NoContent, correct.StatusCode);

        var policiesAfter = await client.GetFromJsonAsync<List<PolicyListItemDto>>("/api/policies?");
        Assert.Empty(policiesAfter!);

        // A wipe this destructive is a bulk ExecuteUpdate — bypassing the audit override — so it
        // must carry its own audit row (rule 27/29).
        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = agencyA.AgencyId;
        var audit = await verify.AuditEntries.AsNoTracking()
            .AnyAsync(a => a.AgencyId == agencyA.AgencyId && a.EntityId == agencyA.AgencyId
                && a.Description!.Contains("پاکسازی کامل"));
        Assert.True(audit);
    }

    [Fact]
    public async Task Requesting_an_otp_without_a_configured_manager_mobile_is_rejected()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(seedContext);
        var mobile = await SeedFullManagerAsync(seedContext, agencyA.AgencyId);

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(mobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", agencyA.AgencyId.ToString());

        var response = await client.PostAsync("/api/settings/agency/data/request-otp", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("موبایل مدیر", await response.Content.ReadAsStringAsync());
        Assert.Empty(_smsSender.SentTo);
    }

    /// <summary>A user with every non-owner permission on the given agency, ready to log in.</summary>
    private static async Task<string> SeedFullManagerAsync(AppDbContext seedContext, Guid agencyId)
    {
        var role = new Role { Name = $"Manager-{Guid.NewGuid():N}"[..16] };
        seedContext.Roles.Add(role);
        await seedContext.SaveChangesAsync();
        foreach (var permission in Permissions.All.Where(p => p != Permissions.PlatformOwner))
        {
            seedContext.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = permission });
        }
        await seedContext.SaveChangesAsync();

        var hasher = new Aqsat.Infrastructure.Security.PasswordHasher();
        var mobile = $"0918{Guid.NewGuid():N}"[..11];
        var user = new AppUser { FullName = "مدیر تستی", Mobile = mobile, PasswordHash = hasher.Hash(DevSeeder.SeededUserPassword), IsActive = true };
        seedContext.Users.Add(user);
        await seedContext.SaveChangesAsync();
        seedContext.UserOrgRoles.Add(new UserOrgRole { UserId = user.Id, OrganizationId = agencyId, RoleId = role.Id });
        await seedContext.SaveChangesAsync();
        return mobile;
    }

    private sealed class RecordingSmsSender : ISmsSender
    {
        public List<string> SentTo { get; } = [];
        private readonly List<string> _texts = [];

        public Task<bool> SendAsync(string mobile, string text, Guid agencyId, CancellationToken ct = default)
        {
            SentTo.Add(mobile);
            _texts.Add(text);
            return Task.FromResult(true);
        }

        public string LastCode()
        {
            var text = _texts[^1];
            var digitsStart = text.IndexOfAny("0123456789".ToCharArray());
            return text.Substring(digitsStart, 6);
        }
    }
}
