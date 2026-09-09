using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Application.Auth;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Domain.Monitoring;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Monitoring;

/// <summary>
/// The monitoring/security endpoints over a real host: the Platform.Owner gate (agency users get
/// 403 — and that 403 itself is recorded as a PermissionDenied security event), the overview and
/// bucketed timeseries aggregations over seeded MetricSamples, the FailedLogin event a wrong
/// password writes, and the alert-rule CRUD + ack/resolve lifecycle.
/// </summary>
[Collection("WebApplicationFactory")]
public class MonitoringEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MonitoringEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    private async Task<HttpClient> LoginClientAsync(DevSeeder.SeededAuthFixture fixture, Guid orgId)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", orgId.ToString());
        return client;
    }

    private static async Task GrantPlatformOwnerAsync(DevSeeder.SeededAuthFixture fixture)
    {
        await using var context = TestDbContextFactory.Create();
        var ownerRole = new Role { Name = $"MonOwner-{Guid.NewGuid():N}"[..24] };
        context.Roles.Add(ownerRole);
        await context.SaveChangesAsync();
        context.RolePermissions.Add(new RolePermission { RoleId = ownerRole.Id, Permission = Permissions.PlatformOwner });
        context.UserOrgRoles.Add(new UserOrgRole
        {
            UserId = fixture.DualAgencyManagerId,
            OrganizationId = fixture.HeadquartersId,
            RoleId = ownerRole.Id,
        });
        await context.SaveChangesAsync();
    }

    /// <summary>Wipes the platform metric table so the aggregation assertions are exact — the
    /// shared test database persists between tests, and the Hangfire server is disabled in the
    /// test host, so nothing else writes samples during the run.</summary>
    private static async Task ClearMetricSamplesAsync()
    {
        await using var context = TestDbContextFactory.Create();
        await context.MetricSamples.IgnoreQueryFilters().ExecuteDeleteAsync();
    }

    private static async Task SeedSamplesAsync(params MetricSample[] samples)
    {
        await using var context = TestDbContextFactory.Create();
        context.MetricSamples.AddRange(samples);
        await context.SaveChangesAsync();
    }

    private static DateTimeOffset MinuteFloor(TimeSpan ago)
    {
        var t = DateTimeOffset.UtcNow.Add(ago);
        return t.AddTicks(-(t.Ticks % TimeSpan.TicksPerMinute));
    }

    [Fact]
    public async Task Monitoring_is_owner_only_and_the_forbidden_call_itself_is_recorded()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        var since = DateTimeOffset.UtcNow;

        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/monitoring/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/monitoring/security/events")).StatusCode);

        // An agency-B login: the seeded staff role there deliberately excludes Platform.Owner
        // (the manager role at agency A carries every permission, including this one).
        var agencyUser = await LoginClientAsync(fixture, fixture.AgencyBId);
        Assert.Equal(HttpStatusCode.Forbidden, (await agencyUser.GetAsync("/api/monitoring/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agencyUser.GetAsync("/api/monitoring/alert-rules")).StatusCode);

        // PermissionDeniedMiddleware wrote the security event on the way out.
        await using var verify = TestDbContextFactory.Create();
        Assert.True(await verify.SecurityEvents.AsNoTracking().AnyAsync(e =>
            e.Type == SecurityEventType.PermissionDenied
            && e.OccurredAt >= since
            && e.Detail.Contains("/api/monitoring/overview")));
    }

    [Fact]
    public async Task A_wrong_password_writes_a_failed_login_security_event()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        var since = DateTimeOffset.UtcNow;

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, "not-the-seeded-password"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        await using var verify = TestDbContextFactory.Create();
        var failed = await verify.SecurityEvents.AsNoTracking()
            .Where(e => e.Type == SecurityEventType.FailedLogin && e.OccurredAt >= since)
            .ToListAsync();
        var row = Assert.Single(failed);
        Assert.Equal(SecuritySeverity.Warning, row.Severity);
        Assert.Equal(fixture.DualAgencyManagerMobile, row.Mobile);
        Assert.NotEmpty(row.Detail);
    }

    [Fact]
    public async Task Overview_aggregates_the_window_and_counts_downtime_against_uptime()
    {
        await ClearMetricSamplesAsync();
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await GrantPlatformOwnerAsync(fixture);

        // Three minutes inside the last hour: 120 requests / 6 errors total, a distinctive latency
        // peak, and one "down" sample so uptime lands at 66.67%.
        await SeedSamplesAsync(
            new MetricSample
            {
                MinuteUtc = MinuteFloor(TimeSpan.FromMinutes(-2)),
                Requests = 50, Errors = 1, ClientErrors = 2,
                LatencyP50Ms = 40, LatencyP95Ms = 220, LatencyP99Ms = 300,
                ProcessMemoryMb = 512, ThreadCount = 20, DiskFreeGb = 40, HealthStatus = "ok",
            },
            new MetricSample
            {
                MinuteUtc = MinuteFloor(TimeSpan.FromMinutes(-4)),
                Requests = 40, Errors = 5, ClientErrors = 1,
                LatencyP50Ms = 60, LatencyP95Ms = 4567, LatencyP99Ms = 6789,
                ProcessMemoryMb = 512, ThreadCount = 20, DiskFreeGb = 40, HealthStatus = "down",
            },
            new MetricSample
            {
                MinuteUtc = MinuteFloor(TimeSpan.FromMinutes(-6)),
                Requests = 30, Errors = 0, ClientErrors = 0,
                LatencyP50Ms = 35, LatencyP95Ms = 180, LatencyP99Ms = 240,
                ProcessMemoryMb = 512, ThreadCount = 20, DiskFreeGb = 40, HealthStatus = "ok",
            });

        var owner = await LoginClientAsync(fixture, fixture.HeadquartersId);
        var overview = await owner.GetFromJsonAsync<MonitoringOverviewDto>("/api/monitoring/overview?range=1h");

        Assert.NotNull(overview);
        Assert.Equal(120, overview!.TotalRequests);
        Assert.Equal(6, overview.TotalErrors);
        Assert.Equal(3, overview.TotalClientErrors);
        Assert.Equal(5.0, overview.ErrorRatePercent);
        Assert.Equal(4567, overview.LatencyP95Ms);
        Assert.Equal(6789, overview.LatencyP99Ms);
        Assert.Equal(66.67, overview.UptimePercent);
        Assert.Equal("ok", overview.HealthStatus);
        Assert.NotNull(overview.LastSampleUtc);
        Assert.NotNull(overview.Security);
    }

    [Fact]
    public async Task Timeseries_buckets_by_the_range_step_instead_of_returning_every_minute()
    {
        await ClearMetricSamplesAsync();
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await GrantPlatformOwnerAsync(fixture);

        // Three consecutive minutes engineered to sit inside ONE 10-minute bucket (the 24h step),
        // so the endpoint must merge them into a single chart point.
        var minutes = ThreeConsecutiveMinutesInOneTenMinuteBucket();
        await SeedSamplesAsync(
            new MetricSample
            {
                MinuteUtc = minutes[0], Requests = 10, Errors = 1, ClientErrors = 0,
                LatencyP95Ms = 100, LatencyP99Ms = 150, ProcessMemoryMb = 100,
                ThreadCount = 10, DiskFreeGb = 30, HealthStatus = "ok",
            },
            new MetricSample
            {
                MinuteUtc = minutes[1], Requests = 20, Errors = 0, ClientErrors = 2,
                LatencyP95Ms = 200, LatencyP99Ms = 250, ProcessMemoryMb = 100,
                ThreadCount = 10, DiskFreeGb = 30, HealthStatus = "ok",
            },
            new MetricSample
            {
                MinuteUtc = minutes[2], Requests = 30, Errors = 0, ClientErrors = 0,
                LatencyP95Ms = 300, LatencyP99Ms = 350, ProcessMemoryMb = 100,
                ThreadCount = 10, DiskFreeGb = 30, HealthStatus = "ok",
            });

        var owner = await LoginClientAsync(fixture, fixture.HeadquartersId);
        var points = await owner.GetFromJsonAsync<List<TimeseriesPointDto>>("/api/monitoring/timeseries?range=24h");

        var point = Assert.Single(points!);
        Assert.Equal(60, point.Requests);
        Assert.Equal(1, point.Errors);
        Assert.Equal(2, point.ClientErrors);
        Assert.Equal(300, point.LatencyP95Ms);
        Assert.Equal(350, point.LatencyP99Ms);
        Assert.Equal(TimeSpan.Zero, point.BucketUtc.Offset);
    }

    [Fact]
    public async Task Alert_rules_crud_and_occurrence_ack_resolve()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        await GrantPlatformOwnerAsync(fixture);
        var owner = await LoginClientAsync(fixture, fixture.HeadquartersId);

        // Create.
        var create = await owner.PostAsJsonAsync("/api/monitoring/alert-rules", new CreateAlertRuleRequest(
            $"تست قانون {Guid.NewGuid():N}"[..8], "ErrorRatePercent", "GreaterThan", 12.5, 15, "Warning", true, false));
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<AlertRuleDto>();
        Assert.NotNull(created);
        Assert.Equal("ErrorRatePercent", created!.Metric);
        Assert.True(created.IsEnabled);

        // Update (threshold + SMS opt-in).
        var update = await owner.PutAsJsonAsync($"/api/monitoring/alert-rules/{created.Id}",
            new UpdateAlertRuleRequest(created.Name, "ErrorRatePercent", "GreaterThan", 20, 15, "Critical", true, true));
        update.EnsureSuccessStatusCode();
        var updated = await update.Content.ReadFromJsonAsync<AlertRuleDto>();
        Assert.Equal(20, updated!.Threshold);
        Assert.True(updated.SmsNotify);
        Assert.Equal("Critical", updated.Severity);

        // Invalid input is rejected with a validation problem, not a 500.
        var invalid = await owner.PutAsJsonAsync($"/api/monitoring/alert-rules/{created.Id}",
            new UpdateAlertRuleRequest(created.Name, "NotAMetric", "GreaterThan", 20, 15, "Critical", true, true));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        // An occurrence on that rule goes Active → Acknowledged → Resolved.
        await using var seedRule = TestDbContextFactory.Create();
        var occurrence = new AlertOccurrence
        {
            RuleId = created.Id,
            Status = AlertStatus.Active,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-3),
            LastSeenAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ObservedValue = 13.2,
            Message = "هشدار تست",
        };
        seedRule.AlertOccurrences.Add(occurrence);
        await seedRule.SaveChangesAsync();

        var alerts = await owner.GetFromJsonAsync<List<AlertOccurrenceDto>>("/api/monitoring/alerts");
        var listed = alerts!.Single(a => a.Id == occurrence.Id);
        Assert.Equal("Active", listed.Status);

        (await owner.PutAsync($"/api/monitoring/alerts/{occurrence.Id}/ack", null)).EnsureSuccessStatusCode();
        (await owner.PutAsync($"/api/monitoring/alerts/{occurrence.Id}/resolve", null)).EnsureSuccessStatusCode();

        await using var verify = TestDbContextFactory.Create();
        var resolved = await verify.AlertOccurrences.AsNoTracking().SingleAsync(o => o.Id == occurrence.Id);
        Assert.Equal(AlertStatus.Resolved, resolved.Status);

        // Delete the rule (soft) — it disappears from the list.
        (await owner.DeleteAsync($"/api/monitoring/alert-rules/{created.Id}")).EnsureSuccessStatusCode();
        var rules = await owner.GetFromJsonAsync<List<AlertRuleDto>>("/api/monitoring/alert-rules");
        Assert.DoesNotContain(rules!, r => r.Id == created.Id);
    }

    /// <summary>
    /// The raw-SQL timeseries groups by DATEDIFF(MINUTE, 0, MinuteUtc) / 10 — 10-minute boundaries
    /// aligned to minute zero. This picks three consecutive past minutes guaranteed to share one
    /// such bucket: if fewer than three minutes of the current bucket have elapsed, it falls back
    /// into the previous bucket.
    /// </summary>
    private static DateTimeOffset[] ThreeConsecutiveMinutesInOneTenMinuteBucket()
    {
        var now = DateTimeOffset.UtcNow;
        var minuteFloor = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMinute));
        var position = (long)(minuteFloor.UtcDateTime - DateTime.UnixEpoch).TotalMinutes % 10;
        var latest = position >= 3 ? minuteFloor : minuteFloor.AddMinutes(-(position + 1));
        return [latest, latest.AddMinutes(-1), latest.AddMinutes(-2)];
    }
}
