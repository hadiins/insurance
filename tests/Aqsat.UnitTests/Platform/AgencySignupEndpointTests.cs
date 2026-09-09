using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Application.Sms;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aqsat.UnitTests.Platform;

/// <summary>Feature 5 — self-serve agency signup: mobile OTP, the org is created PENDING
/// (IsActive=false), login stays closed until the platform owner activates the agency, and every
/// request is gated by the owner's PlatformSignupSettings switch (absent row = closed).</summary>
[Collection("WebApplicationFactory")]
public class AgencySignupEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly RecordingSmsSender _smsSender = new();
    private readonly WebApplicationFactory<Program> _factory;

    public AgencySignupEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services => services.AddSingleton<ISmsSender>(_smsSender));
        });
    }

    /// <summary>The gate is a DB singleton, so each test seeds the state it needs — the shared
    /// persistent test database keeps whatever the previous test left behind.</summary>
    private static async Task SetSignupOpenAsync(bool open)
    {
        using var context = TestDbContextFactory.Create();
        var settings = await context.PlatformSignupSettings.SingleOrDefaultAsync();
        if (settings is null)
        {
            context.PlatformSignupSettings.Add(new PlatformSignupSettings { AllowAgencySignup = open });
        }
        else
        {
            settings.AllowAgencySignup = open;
        }

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Signup_is_closed_by_default_and_the_endpoints_reject_with_a_Persian_message()
    {
        await SetSignupOpenAsync(false);
        var client = _factory.CreateClient();

        var status = await client.GetFromJsonAsync<SignupStatusDto>("/api/signup/status");
        Assert.False(status!.IsOpen);

        var otp = await client.PostAsJsonAsync("/api/signup/request-otp", new SignupRequestOtpRequest("09121234567"));
        Assert.Equal(HttpStatusCode.BadRequest, otp.StatusCode);
        Assert.Contains("فعال نیست", await otp.Content.ReadAsStringAsync());

        var signup = await client.PostAsJsonAsync("/api/signup", new AgencySignupRequest(
            "نمایندگی", "مدیر", "09121234567", "123456", "Password-1", null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, signup.StatusCode);
        Assert.Empty(_smsSender.SentTo);
    }

    [Fact]
    public async Task Signup_creates_a_pending_agency_with_a_working_manager_login_after_approval()
    {
        await SetSignupOpenAsync(true);
        using var seedContext = TestDbContextFactory.Create();
        var hq = new Organization { Level = OrganizationLevel.Headquarters, Code = $"HQ-{Guid.NewGuid():N}"[..10], Name = "دفتر مرکزی آزمایشی", IsActive = true };
        seedContext.Organizations.Add(hq);
        await seedContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var mobile = $"091{Random.Shared.Next(10_000_000, 99_999_999)}";

        var otp = await client.PostAsJsonAsync("/api/signup/request-otp", new SignupRequestOtpRequest(mobile));
        otp.EnsureSuccessStatusCode();
        Assert.Equal(mobile, _smsSender.SentTo[^1]);

        var signup = await client.PostAsJsonAsync("/api/signup", new AgencySignupRequest(
            $"نمایندگی آزمایشی {mobile[^4..]}", "مدیر جدید", mobile, _smsSender.LastCode(),
            "Password-1", "تهران", "تهران", "بیمهٔ آزمایشی"));
        Assert.Equal(HttpStatusCode.Created, signup.StatusCode);

        // The org exists, is PENDING, sits under HQ, and carries the auto-generated SU- code.
        var nameSuffix = mobile[^4..];
        await using var verify = TestDbContextFactory.Create();
        var org = await verify.Organizations.AsNoTracking()
            .Include(o => o.Parent)
            .SingleAsync(o => o.Code.StartsWith("SU-") && o.Name.Contains(nameSuffix));
        Assert.False(org.IsActive);
        Assert.Equal(OrganizationLevel.Agency, org.Level);
        // The shared test database holds many HQs from other fixtures — the signup controller
        // attaches to whichever HQ exists, so only the level is asserted, not the exact id.
        Assert.Equal(OrganizationLevel.Headquarters, org.Parent!.Level);

        var membership = await verify.UserOrgRoles.AsNoTracking()
            .Include(m => m.User)
            .Include(m => m.Role).ThenInclude(r => r.RolePermissions)
            .SingleAsync(m => m.User.Mobile == mobile);
        Assert.Equal(org.Id, membership.OrganizationId);
        Assert.Contains(membership.Role.RolePermissions, p => p.Permission == Permissions.PolicyRead);
        Assert.DoesNotContain(membership.Role.RolePermissions, p => p.Permission == Permissions.PlatformOwner);

        // The signup is audited in the same unit of work (rules 27-29).
        AgencyContext.Current = org.Id;
        var audited = await verify.AuditEntries.AsNoTracking()
            .AnyAsync(a => a.AgencyId == org.Id && a.EntityId == org.Id && a.Description!.Contains("ثبت‌نام خودکار"));
        Assert.True(audited);

        // Before activation the manager's login is refused with the pending message.
        var blockedLogin = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(mobile, "Password-1"));
        Assert.Equal(HttpStatusCode.Unauthorized, blockedLogin.StatusCode);
        var body = await blockedLogin.Content.ReadFromJsonAsync<ProblemDetailsLite>();
        Assert.StartsWith("نمایندگی شما", body!.Title, StringComparison.Ordinal);

        // The owner activates the agency from «مدیریت نمایندگی‌ها»…
        var ownerClient = await CreateOwnerClientAsync(hq.Id);
        var update = await ownerClient.PutAsJsonAsync($"/api/platform/agencies/{org.Id}", new UpdateAgencyRequest(
            org.Name, org.Province, org.City, org.InsurerName, IsActive: true));
        update.EnsureSuccessStatusCode();

        // …and the manager logs in with the default role's permissions.
        var managerClient = _factory.CreateClient();
        var login = await managerClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(mobile, "Password-1"));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        managerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        managerClient.DefaultRequestHeaders.Add("X-Organization-Id", org.Id.ToString());

        var me = await managerClient.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.Equal(org.Id, me!.ActiveOrganizationId);
        Assert.Contains(Permissions.PolicyRead, me.Permissions);
        Assert.DoesNotContain(Permissions.PlatformOwner, me.Permissions);
    }

    [Fact]
    public async Task Invalid_input_is_rejected_without_sending_the_sms_or_creating_anything()
    {
        await SetSignupOpenAsync(true);
        using var seedContext = TestDbContextFactory.Create();
        var hq = new Organization { Level = OrganizationLevel.Headquarters, Code = $"HQ-{Guid.NewGuid():N}"[..10], Name = "دفتر مرکزی آزمایشی", IsActive = true };
        seedContext.Organizations.Add(hq);
        await seedContext.SaveChangesAsync();

        // An already-registered mobile is refused BEFORE the OTP is sent.
        var client = _factory.CreateClient();
        var takenMobile = $"091{Random.Shared.Next(10_000_000, 99_999_999)}";
        seedContext.Users.Add(new AppUser
        {
            FullName = "کاربر موجود",
            Mobile = takenMobile,
            PasswordHash = new Aqsat.Infrastructure.Security.PasswordHasher().Hash("Password-1"),
            IsActive = true,
        });
        await seedContext.SaveChangesAsync();

        var takenOtp = await client.PostAsJsonAsync("/api/signup/request-otp", new SignupRequestOtpRequest(takenMobile));
        Assert.Equal(HttpStatusCode.BadRequest, takenOtp.StatusCode);
        Assert.Contains("قبلاً ثبت شده", await takenOtp.Content.ReadAsStringAsync());
        Assert.DoesNotContain(takenMobile, _smsSender.SentTo);

        // Bad mobile shape.
        var badMobile = await client.PostAsJsonAsync("/api/signup/request-otp", new SignupRequestOtpRequest("12345"));
        Assert.Equal(HttpStatusCode.BadRequest, badMobile.StatusCode);

        // Happy-path OTP, then each invalid signup variant must fail with nothing created. The
        // name carries a unique tag — the shared test database accumulates other fixtures'
        // agencies, so the "nothing was created" count must be scoped to this run's tag.
        var tag = Guid.NewGuid().ToString("N")[..8];
        var mobile = $"091{Random.Shared.Next(10_000_000, 99_999_999)}";
        await client.PostAsJsonAsync("/api/signup/request-otp", new SignupRequestOtpRequest(mobile));
        var code = _smsSender.LastCode();

        var wrongOtp = await client.PostAsJsonAsync("/api/signup", new AgencySignupRequest(
            $"نمایندگی {tag}", "مدیر", mobile, "000000", "Password-1", null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, wrongOtp.StatusCode);

        var shortPassword = await client.PostAsJsonAsync("/api/signup", new AgencySignupRequest(
            $"نمایندگی {tag}", "مدیر", mobile, code, "Short-1", null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, shortPassword.StatusCode);

        var badProvince = await client.PostAsJsonAsync("/api/signup", new AgencySignupRequest(
            $"نمایندگی {tag}", "مدیر", mobile, code, "Password-1", "آتلانتیس", null, null));
        Assert.Equal(HttpStatusCode.BadRequest, badProvince.StatusCode);

        await using var verify = TestDbContextFactory.Create();
        var orgs = await verify.Organizations.AsNoTracking()
            .CountAsync(o => o.Level == OrganizationLevel.Agency && o.Name.Contains(tag));
        Assert.Equal(0, orgs);
    }

    [Fact]
    public async Task A_suspended_agency_locks_its_users_out_with_an_explicit_403()
    {
        // Seed a fully working agency + manager, then flip the org to inactive — the middleware
        // must refuse every authenticated request with a message, never an empty scope (rule 17).
        await using var seedContext = TestDbContextFactory.Create();
        var hq = new Organization { Level = OrganizationLevel.Headquarters, Code = $"HQ-{Guid.NewGuid():N}"[..10], Name = "دفتر مرکزی آزمایشی", IsActive = true };
        seedContext.Organizations.Add(hq);
        await seedContext.SaveChangesAsync();

        var org = new Organization { Level = OrganizationLevel.Agency, ParentId = hq.Id, Code = $"SU-{Guid.NewGuid():N}"[..11], Name = "نمایندگی معلق", IsActive = false };
        seedContext.Organizations.Add(org);
        await seedContext.SaveChangesAsync();

        var role = new Role { Name = $"Manager-{Guid.NewGuid():N}"[..16] };
        seedContext.Roles.Add(role);
        await seedContext.SaveChangesAsync();
        seedContext.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = Permissions.PolicyRead });
        await seedContext.SaveChangesAsync();

        var mobile = $"091{Random.Shared.Next(10_000_000, 99_999_999)}";
        var manager = new AppUser { FullName = "مدیر معلق", Mobile = mobile, PasswordHash = new Aqsat.Infrastructure.Security.PasswordHasher().Hash(DevSeeder.SeededUserPassword), IsActive = true };
        seedContext.Users.Add(manager);
        await seedContext.SaveChangesAsync();
        seedContext.UserOrgRoles.Add(new UserOrgRole { UserId = manager.Id, OrganizationId = org.Id, RoleId = role.Id });
        await seedContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(mobile, DevSeeder.SeededUserPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<ProblemDetailsLite>();
        Assert.StartsWith("نمایندگی شما", body!.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_owner_flips_the_signup_switch_from_the_panel_and_it_takes_effect_instantly()
    {
        // The switch replaced editing appsettings on the server (owner request 2026-09-08): PUT
        // from the owner's client must flip the PUBLIC status endpoint on the very next request.
        await SetSignupOpenAsync(false);
        var client = _factory.CreateClient();

        // The settings endpoint is Platform.Owner-only — anonymous callers are refused.
        var anonymousGet = await client.GetAsync("/api/platform/signup-settings");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousGet.StatusCode);

        await using var seedContext = TestDbContextFactory.Create();
        var hq = new Organization { Level = OrganizationLevel.Headquarters, Code = $"HQ-{Guid.NewGuid():N}"[..10], Name = "دفتر مرکزی آزمایشی", IsActive = true };
        seedContext.Organizations.Add(hq);
        await seedContext.SaveChangesAsync();
        var ownerClient = await CreateOwnerClientAsync(hq.Id);

        var enabled = await ownerClient.PutAsJsonAsync(
            "/api/platform/signup-settings", new UpdatePlatformSignupSettingsRequest(true));
        enabled.EnsureSuccessStatusCode();
        var enabledDto = await enabled.Content.ReadFromJsonAsync<PlatformSignupSettingsDto>();
        Assert.True(enabledDto!.AllowAgencySignup);
        Assert.NotNull(enabledDto.UpdatedAtUtc);
        Assert.True((await client.GetFromJsonAsync<SignupStatusDto>("/api/signup/status"))!.IsOpen);

        var disabled = await ownerClient.PutAsJsonAsync(
            "/api/platform/signup-settings", new UpdatePlatformSignupSettingsRequest(false));
        disabled.EnsureSuccessStatusCode();
        Assert.False((await client.GetFromJsonAsync<SignupStatusDto>("/api/signup/status"))!.IsOpen);
    }

    private async Task<HttpClient> CreateOwnerClientAsync(Guid hqId)
    {
        await using var context = TestDbContextFactory.Create();
        var ownerRole = new Role { Name = $"Owner-{Guid.NewGuid():N}"[..12], IsSystemRole = true };
        context.Roles.Add(ownerRole);
        await context.SaveChangesAsync();
        context.RolePermissions.Add(new RolePermission { RoleId = ownerRole.Id, Permission = Permissions.PlatformOwner });
        await context.SaveChangesAsync();

        var mobile = $"091{Random.Shared.Next(10_000_000, 99_999_999)}";
        var user = new AppUser
        {
            FullName = "مالک تستی",
            Mobile = mobile,
            PasswordHash = new Aqsat.Infrastructure.Security.PasswordHasher().Hash(DevSeeder.SeededUserPassword),
            IsActive = true,
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        context.UserOrgRoles.Add(new UserOrgRole { UserId = user.Id, OrganizationId = hqId, RoleId = ownerRole.Id });
        await context.SaveChangesAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(mobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", hqId.ToString());
        return client;
    }

    private sealed record ProblemDetailsLite(string? Title);

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
