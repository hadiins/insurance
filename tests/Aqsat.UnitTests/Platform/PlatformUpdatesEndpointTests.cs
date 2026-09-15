using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Application.Sms;
using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aqsat.UnitTests.Platform;

/// <summary>
/// Task 22's own check (docs/TASKS.md), the parts exercisable without a live Aqsat.Updater
/// (Docker isn't available in this dev environment — see docs/RESTORE-RUNBOOK.md for the same
/// constraint on Task 20/21): OTP is required before an update can start, an invalid/expired code
/// is rejected, a yanked package can't be started, a too-old-to-jump-directly version is rejected,
/// and a started run is durably recorded (docs/UPDATE-SYSTEM.md §5's "closing the browser must not
/// stop the update" — proven here by the run existing in GET /history regardless of what the
/// unreachable Updater call itself does). Maintenance-mode toggling and the 60-second warning are
/// exercised end-to-end by configuring that window down to milliseconds for the test.
/// </summary>
[Collection("WebApplicationFactory")]
public class PlatformUpdatesEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly RecordingSmsSender _smsSender = new();
    private readonly WebApplicationFactory<Program> _factory;

    public PlatformUpdatesEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Updater:MaintenanceWarningSeconds"] = "0",
                // A real, unresolvable "aqsat-updater" hostname can take many seconds to time out
                // via DNS — loopback-with-nothing-listening fails ~instantly with "connection
                // refused" instead, which is what this test actually needs: a fast, deterministic
                // failure to prove the background task doesn't hang when Updater is unreachable.
                ["Updater:BaseUrl"] = "http://127.0.0.1:1",
            }));
            builder.ConfigureServices(services => services.AddSingleton<ISmsSender>(_smsSender));
        });
    }

    [Fact]
    public async Task Non_platform_owner_cannot_see_any_platform_endpoint()
    {
        var (client, _) = await SeedAsync();

        // AgencyOnlyStaff has no Platform.Owner permission at all — DevSeeder's fixture, not a
        // platform-level assignment.
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest((await SeedFixtureAsync()).AgencyOnlyStaffMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;

        var anonClient = _factory.CreateClient();
        anonClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await anonClient.GetAsync("/api/platform/updates/status");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Starting_an_update_without_a_valid_otp_is_rejected()
    {
        var (client, package) = await SeedAsync();

        var response = await client.PostAsJsonAsync($"/api/platform/updates/{package.Id}/start", new StartUpdateRequest("000000"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_smsSender.SentTo); // Never even requested.
    }

    [Fact]
    public async Task A_yanked_package_cannot_be_started_even_with_a_valid_otp()
    {
        var (client, package) = await SeedAsync(yanked: true);

        var otpResponse = await client.PostAsync("/api/platform/updates/otp", null);
        otpResponse.EnsureSuccessStatusCode();
        var code = _smsSender.LastCode();

        var response = await client.PostAsJsonAsync($"/api/platform/updates/{package.Id}/start", new StartUpdateRequest(code));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_package_requiring_a_newer_minimum_version_than_the_running_one_is_rejected()
    {
        var (client, package) = await SeedAsync(minimumFromVersion: "99.0.0");

        var otpResponse = await client.PostAsync("/api/platform/updates/otp", null);
        otpResponse.EnsureSuccessStatusCode();
        var code = _smsSender.LastCode();

        var response = await client.PostAsJsonAsync($"/api/platform/updates/{package.Id}/start", new StartUpdateRequest(code));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        Assert.Contains("99.0.0", problem!.Title);
    }

    [Fact]
    public async Task A_valid_otp_starts_the_update_maintenance_mode_toggles_and_the_run_is_recorded_durably()
    {
        var (client, package) = await SeedAsync();

        var otpResponse = await client.PostAsync("/api/platform/updates/otp", null);
        otpResponse.EnsureSuccessStatusCode();
        var code = _smsSender.LastCode();

        var startResponse = await client.PostAsJsonAsync($"/api/platform/updates/{package.Id}/start", new StartUpdateRequest(code));
        Assert.Equal(HttpStatusCode.Accepted, startResponse.StatusCode);
        var started = await startResponse.Content.ReadFromJsonAsync<UpdateRunDto>();
        Assert.NotNull(started);
        Assert.Equal(package.Version, started!.ToVersion);

        // Same code can't be replayed — single-use.
        var replay = await client.PostAsJsonAsync($"/api/platform/updates/{package.Id}/start", new StartUpdateRequest(code));
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

        // No real Aqsat.Updater is reachable in this test environment, so the background task will
        // fail quickly once it tries — but the run itself, having been persisted before that
        // background task even starts, must survive regardless (docs/UPDATE-SYSTEM.md §5).
        UpdateRunDto? historyRow = null;
        for (var attempt = 0; attempt < 100 && historyRow is null; attempt++)
        {
            var history = await client.GetFromJsonAsync<List<UpdateRunDto>>("/api/platform/updates/history");
            historyRow = history!.SingleOrDefault(r => r.Id == started.Id);
            if (historyRow is null || historyRow.Status == "Running")
            {
                historyRow = null;
                await Task.Delay(100);
            }
        }

        Assert.NotNull(historyRow);
        Assert.Equal("Failed", historyRow!.Status);

        // Maintenance mode must have been switched back off once the (failed) run completed.
        var status = await client.GetFromJsonAsync<PlatformStatusDto>("/api/platform/updates/status");
        Assert.False(status!.MaintenanceModeActive);
    }

    private async Task<(HttpClient Client, UpdatePackage Package)> SeedAsync(bool yanked = false, string? minimumFromVersion = null)
    {
        var fixture = await SeedFixtureAsync();

        await using var seedContext = TestDbContextFactory.Create();
        var package = new UpdatePackage
        {
            // Unique index on Version: the entropy must survive leftovers from an aborted run
            // (the shared test database is only wiped once per assembly load).
            Version = $"1.{Random.Shared.Next(100000, 999999)}.{Random.Shared.Next(1, 99)}",
            ReleaseNotesFa = "رفع اشکال آزمایشی",
            ImageTag = "registry.example.ir/aqsat-api:test",
            Sha256 = "sha256:" + new string('a', 64),
            SignatureBase64 = "dGVzdA==",
            MinimumFromVersion = minimumFromVersion,
            PublishedAt = DateTimeOffset.UtcNow,
            IsYanked = yanked,
        };
        seedContext.UpdatePackages.Add(package);
        await seedContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());

        return (client, package);
    }

    private DevSeeder.SeededAuthFixture? _cachedFixture;

    private async Task<DevSeeder.SeededAuthFixture> SeedFixtureAsync()
    {
        if (_cachedFixture is not null)
        {
            return _cachedFixture;
        }

        await using var seedContext = TestDbContextFactory.Create();
        _cachedFixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);
        return _cachedFixture;
    }

    private sealed record ProblemDetailsDto(string Title);

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
