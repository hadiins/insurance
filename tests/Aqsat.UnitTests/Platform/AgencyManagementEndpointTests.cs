using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Jobs;
using Aqsat.Infrastructure.Seed;
using Aqsat.Infrastructure.Stats;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Platform;

/// <summary>
/// The owner's agency-management surface (پروندهٔ نمایندگی): paged/filtered list with lifetime
/// stat columns off the AgencyStatsDaily rollup, live per-agency profile numbers computed inside
/// the agency's RLS scope, and owner-side identity editing. Stats assertions are always scoped to
/// freshly-created agencies — the shared dev database carries hundreds of leftover fixture rows,
/// so global totals are only ever asserted as non-zero.
/// </summary>
[Collection("WebApplicationFactory")]
public class AgencyManagementEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AgencyManagementEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    /// <summary>Same Platform.Owner seeding PlatformPaymentEndpointTests uses — the whole
    /// api/platform/agencies surface shares that single gate.</summary>
    private async Task<HttpClient> CreateOwnerClientAsync()
    {
        await using var context = TestDbContextFactory.Create();
        var hq = new Organization { Level = OrganizationLevel.Headquarters, Code = $"HQ-{Guid.NewGuid():N}"[..10], Name = "دفتر مرکزی آزمایشی", IsActive = true };
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

    /// <summary>Creates a distinct agency with Province set, plus rollup rows carrying known
    /// lifetime sums — the list's stat columns must reproduce them exactly.</summary>
    private static async Task<(Guid Id, string Code)> CreateAgencyWithStatsAsync(
        string province, bool isActive, int policies, int sms, int inquiryPayments, decimal revenue, int inquiryCalls)
    {
        await using var context = TestDbContextFactory.Create();
        var code = $"AG-{Guid.NewGuid():N}"[..12];
        var org = new Organization
        {
            Level = OrganizationLevel.Agency,
            Code = code,
            Name = $"نمایندگی آماری {code}",
            Province = province,
            City = "شهر آماری",
            InsurerName = "بیمهٔ آماری",
            IsActive = isActive,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        for (var day = 0; day < 3; day++)
        {
            context.AgencyStatsDaily.Add(new AgencyStatsDaily
            {
                AgencyId = org.Id,
                StatDate = today.AddDays(-day),
                PoliciesIssued = policies,
                SmsSentCount = sms,
                InquiryPaymentsCount = inquiryPayments,
                InquiryRevenueToman = revenue,
                InquiryCallsCount = inquiryCalls,
            });
        }

        await context.SaveChangesAsync();
        return (org.Id, code);
    }

    private static async Task<(Guid AgencyId, Guid CustomerId)> SeedAgencyWithActivityAsync()
    {
        await using var context = TestDbContextFactory.Create();
        var (agencyA, _) = await DevSeeder.SeedTwoAgenciesAsync(context);

        Aqsat.Infrastructure.Persistence.AgencyContext.Current = agencyA.AgencyId;
        // The rollup buckets calls and payments by Iran-local day (UTC+3:30) — between 20:30 and
        // 24:00 UTC that is tomorrow's UTC date, so seeding and asserting on the UTC date flips
        // the buckets. Everything here runs on the Iran-local clock instead.
        var iranNow = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(210));
        var today = DateOnly.FromDateTime(iranNow.DateTime);
        context.ApiIrCallLogs.AddRange(
            new ApiIrCallLog { AgencyId = agencyA.AgencyId, Service = "SendSms", Success = true, CostToman = 115m, CalledAt = iranNow },
            new ApiIrCallLog { AgencyId = agencyA.AgencyId, Service = "SendSms", Success = true, CostToman = 115m, CalledAt = iranNow },
            new ApiIrCallLog { AgencyId = agencyA.AgencyId, Service = "SmsOTP", Success = true, CostToman = 115m, CalledAt = iranNow },
            new ApiIrCallLog { AgencyId = agencyA.AgencyId, Service = "ShahkarLite", Success = true, CostToman = 550m, CalledAt = iranNow },
            new ApiIrCallLog { AgencyId = agencyA.AgencyId, Service = "ChequeColor", Success = true, CostToman = 1100m, CalledAt = iranNow });
        context.CustomerPortalInvitations.Add(new CustomerPortalInvitation
        {
            AgencyId = agencyA.AgencyId,
            CustomerId = agencyA.CustomerId,
            Token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
            InquiryFeeToman = 25_000m,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(2),
            Status = PortalInvitationStatus.Paid,
            PaidAtUtc = iranNow,
            PaidAmountToman = 25_000m,
        });
        context.Policies.Add(new Policy
        {
            AgencyId = agencyA.AgencyId,
            PolicyNumber = $"POL-STATS-{Guid.NewGuid():N}"[..20],
            InsuranceLineId = await context.InsuranceLines.Select(l => l.Id).FirstAsync(),
            CustomerId = agencyA.CustomerId,
            ContractName = "قرارداد آماری",
            IssueDate = today,
            StartDate = today,
            EndDate = today.AddYears(1),
            NetPremium = 1,
            ServiceFee = 0,
            DownPayment = 0,
            InstallmentCount = 1,
            Status = PolicyStatus.Active,
        });
        // A second policy so the rollup test's PoliciesIssued >= 2 assertion stands on this
        // fixture alone, not on whatever dates other seeders happened to use.
        context.Policies.Add(new Policy
        {
            AgencyId = agencyA.AgencyId,
            PolicyNumber = $"POL-STATS-{Guid.NewGuid():N}"[..20],
            InsuranceLineId = await context.InsuranceLines.Select(l => l.Id).FirstAsync(),
            CustomerId = agencyA.CustomerId,
            ContractName = "قرارداد آماری",
            IssueDate = today,
            StartDate = today,
            EndDate = today.AddYears(1),
            NetPremium = 1,
            ServiceFee = 0,
            DownPayment = 0,
            InstallmentCount = 1,
            Status = PolicyStatus.Active,
        });
        await context.SaveChangesAsync();
        return (agencyA.AgencyId, agencyA.CustomerId);
    }

    [Fact]
    public async Task List_search_finds_the_agency_and_reports_its_lifetime_rollup_sums()
    {
        var (id, code) = await CreateAgencyWithStatsAsync("یزد", true, policies: 2, sms: 4, inquiryPayments: 1, revenue: 25_000m, inquiryCalls: 3);

        var client = await CreateOwnerClientAsync();
        var page = await client.GetFromJsonAsync<AgencyListPageDto>($"/api/platform/agencies?search={code}");

        Assert.NotNull(page);
        Assert.True(page.TotalCount >= 1);
        var row = Assert.Single(page.Rows, r => r.Id == id);
        Assert.Equal(6, row.PoliciesTotal);
        Assert.Equal(12, row.SmsSentTotal);
        Assert.Equal(3, row.InquiryPaymentsTotal);
        Assert.Equal(9, row.InquiryCallsTotal);
        Assert.Equal(75_000m, row.InquiryRevenueToman);
        Assert.Equal("یزد", row.Province);
    }

    [Fact]
    public async Task List_filters_by_province_and_active_status()
    {
        var (activeId, _) = await CreateAgencyWithStatsAsync("یزد", true, 1, 1, 1, 1m, 1);
        var (inactiveId, _) = await CreateAgencyWithStatsAsync("تهران", false, 1, 1, 1, 1m, 1);

        var client = await CreateOwnerClientAsync();
        var page = await client.GetFromJsonAsync<AgencyListPageDto>("/api/platform/agencies?province=یزد&isActive=true");

        Assert.NotNull(page);
        Assert.Contains(page.Rows, r => r.Id == activeId);
        Assert.DoesNotContain(page.Rows, r => r.Id == inactiveId);
    }

    [Fact]
    public async Task List_requires_the_owner_permission()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/platform/agencies");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Filters_offer_the_canonical_province_list()
    {
        var client = await CreateOwnerClientAsync();
        var options = await client.GetFromJsonAsync<AgencyFilterOptionsDto>("/api/platform/agencies/filters");

        Assert.NotNull(options);
        Assert.Equal(31, options.Provinces.Count);
        Assert.Contains("تهران", options.Provinces);
        Assert.Contains("یزد", options.Provinces);
    }

    [Fact]
    public async Task Profile_reports_live_totals_and_a_twelve_month_jalali_trend()
    {
        var (agencyId, _) = await SeedAgencyWithActivityAsync();

        var client = await CreateOwnerClientAsync();
        var profile = await client.GetFromJsonAsync<AgencyProfileDto>($"/api/platform/agencies/{agencyId}");

        Assert.NotNull(profile);
        // One from the seeder + one seeded here; anything less means RLS blanked the read (rule 17).
        Assert.True(profile.PoliciesIssuedTotal >= 2);
        Assert.True(profile.PoliciesThisMonth >= 2);
        Assert.Equal(3, profile.SmsSentTotal);
        Assert.Equal(345m, profile.SmsCostToman);
        Assert.Equal(1, profile.InquiryPaymentsTotal);
        Assert.Equal(25_000m, profile.InquiryRevenueToman);
        Assert.Equal(2, profile.InquiryCallsTotal);
        Assert.Equal(1650m, profile.InquiryCallCostToman);
        Assert.Equal(12, profile.MonthlyTrend.Count);
        // The current Jalali month bucket must carry today's activity.
        var current = profile.MonthlyTrend[^1];
        Assert.True(current.Policies >= 2);
        Assert.True(current.Sms >= 3);
        Assert.Equal(1, current.InquiryPayments);
    }

    [Fact]
    public async Task Profile_of_an_unknown_agency_answers_404_in_persian()
    {
        var client = await CreateOwnerClientAsync();
        var response = await client.GetAsync($"/api/platform/agencies/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("نمایندگی", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Update_changes_identity_fields_and_rejects_an_unknown_province()
    {
        var (id, _) = await CreateAgencyWithStatsAsync("یزد", true, 1, 1, 1, 1m, 1);

        var client = await CreateOwnerClientAsync();
        var ok = await client.PutAsJsonAsync($"/api/platform/agencies/{id}",
            new UpdateAgencyRequest("نمایندگی ویرایش‌شده", "فارس", "شهر جدید", "بیمهٔ جدید", false));
        ok.EnsureSuccessStatusCode();
        var updated = await ok.Content.ReadFromJsonAsync<AgencyDto>();
        Assert.Equal("نمایندگی ویرایش‌شده", updated!.Name);
        Assert.Equal("فارس", updated.Province);
        Assert.False(updated.IsActive);

        await using var verify = TestDbContextFactory.Create();
        var row = await verify.Organizations.AsNoTracking().SingleAsync(o => o.Id == id);
        Assert.Equal("فارس", row.Province);
        Assert.Equal("شهر جدید", row.City);
        Assert.False(row.IsActive);

        var rejected = await client.PutAsJsonAsync($"/api/platform/agencies/{id}",
            new UpdateAgencyRequest("نام", "آتلانتیس", null, null, true));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Fact]
    public async Task Rollup_job_aggregates_an_agencys_activity_into_daily_rows()
    {
        var (agencyId, _) = await SeedAgencyWithActivityAsync();

        // The job loops every Agency org in the shared dev database; the assertion only reads this
        // one agency's rows back, so leftover fixture data cannot break it.
        await using var jobContext = TestDbContextFactory.Create();
        var job = new AgencyStatsRollupJob(jobContext, new AgencyStatsService(jobContext), TimeProvider.System);
        await job.RunAsync().WaitAsync(TimeSpan.FromMinutes(5));

        await using var verify = TestDbContextFactory.Create();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(210)).DateTime);
        var todayRow = await verify.AgencyStatsDaily.AsNoTracking()
            .SingleOrDefaultAsync(s => s.AgencyId == agencyId && s.StatDate == today);

        Assert.NotNull(todayRow);
        Assert.True(todayRow.PoliciesIssued >= 2);
        Assert.True(todayRow.SmsSentCount >= 3);
        Assert.Equal(1, todayRow.InquiryPaymentsCount);
        Assert.Equal(25_000m, todayRow.InquiryRevenueToman);
        Assert.Equal(2, todayRow.InquiryCallsCount);
    }

    [Fact]
    public async Task Summary_counts_agencies_and_provides_a_province_breakdown()
    {
        var (id, _) = await CreateAgencyWithStatsAsync("کرمان", true, policies: 2, sms: 1, inquiryPayments: 1, revenue: 5_000m, inquiryCalls: 1);

        var client = await CreateOwnerClientAsync();
        var summary = await client.GetFromJsonAsync<AgencyPlatformSummaryDto>("/api/platform/agencies/summary");

        Assert.NotNull(summary);
        Assert.True(summary.TotalAgencies >= 1);
        Assert.True(summary.PoliciesTotal >= 6);
        var kerman = summary.ByProvince.FirstOrDefault(p => p.Province == "کرمان");
        Assert.NotNull(kerman);
        Assert.True(kerman.AgencyCount >= 1);
        Assert.True(kerman.PoliciesTotal >= 6);
    }
}
