using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Concurrency;

/// <summary>
/// Task 11's own check (docs/TASKS.md): two clients, same record — presence and lock contention are
/// covered by hitting the real HTTP+SignalR pipeline, same pattern as AuthenticationTests. "Second
/// edit attempt is refused with the holder's name", "force-release notifies the holder immediately",
/// and the mandatory reason + audit trail are all exercised against the real LocalDB database.
/// </summary>
[Collection("WebApplicationFactory")]
public class LockEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LockEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Second_user_trying_to_acquire_an_active_lock_is_told_who_holds_it()
    {
        var (fixture, staffClient, managerClient) = await SeedAndLoginBothAsync();
        var entityId = Guid.NewGuid();

        var firstAcquire = await staffClient.PostAsJsonAsync("/api/locks/acquire", new AcquireLockRequest("Policy", entityId, 10));
        firstAcquire.EnsureSuccessStatusCode();
        var firstResult = await firstAcquire.Content.ReadFromJsonAsync<LockStatusDto>();
        Assert.True(firstResult!.AcquiredByMe);

        var secondAcquire = await managerClient.PostAsJsonAsync("/api/locks/acquire", new AcquireLockRequest("Policy", entityId, 10));
        secondAcquire.EnsureSuccessStatusCode();
        var secondResult = await secondAcquire.Content.ReadFromJsonAsync<LockStatusDto>();

        Assert.False(secondResult!.AcquiredByMe);
        Assert.Equal(firstResult.LockedByUserId, secondResult.LockedByUserId);
        Assert.Equal("کارمند بدون دسترسی پرداخت", secondResult.LockedByDisplayName);
    }

    [Fact]
    public async Task Releasing_a_lock_lets_someone_else_acquire_it_immediately()
    {
        var (fixture, staffClient, managerClient) = await SeedAndLoginBothAsync();
        var entityId = Guid.NewGuid();

        await staffClient.PostAsJsonAsync("/api/locks/acquire", new AcquireLockRequest("Policy", entityId, 10));
        var releaseResponse = await staffClient.PostAsJsonAsync("/api/locks/release", new ReleaseLockRequest("Policy", entityId));
        Assert.Equal(HttpStatusCode.NoContent, releaseResponse.StatusCode);

        var acquireAfterRelease = await managerClient.PostAsJsonAsync("/api/locks/acquire", new AcquireLockRequest("Policy", entityId, 10));
        var result = await acquireAfterRelease.Content.ReadFromJsonAsync<LockStatusDto>();
        Assert.True(result!.AcquiredByMe);
    }

    [Fact]
    public async Task Force_release_rejects_a_reason_shorter_than_ten_characters()
    {
        var (fixture, staffClient, managerClient) = await SeedAndLoginBothAsync();
        var entityId = Guid.NewGuid();

        var acquire = await staffClient.PostAsJsonAsync("/api/locks/acquire", new AcquireLockRequest("Policy", entityId, 10));
        var lockId = (await acquire.Content.ReadFromJsonAsync<LockStatusDto>())!.LockId;

        var response = await managerClient.PostAsJsonAsync($"/api/locks/{lockId}/force-release", new ForceReleaseLockRequest("کوتاه"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Force_release_frees_the_lock_writes_an_audit_row_and_notifies_the_holder_in_real_time()
    {
        var (fixture, staffClient, managerClient) = await SeedAndLoginBothAsync();
        var entityId = Guid.NewGuid(); // EntityType "Policy" resolves PolicyId to EntityId directly.

        var acquire = await staffClient.PostAsJsonAsync("/api/locks/acquire", new AcquireLockRequest("Policy", entityId, 10));
        var lockId = (await acquire.Content.ReadFromJsonAsync<LockStatusDto>())!.LockId;

        var staffToken = staffClient.DefaultRequestHeaders.Authorization!.Parameter!;
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri("http://localhost/hubs/presence"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(staffToken);
            })
            .Build();

        var revoked = new TaskCompletionSource<LockRevokedPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<LockRevokedPayload>("LockRevoked", payload => revoked.TrySetResult(payload));
        await connection.StartAsync();

        const string reason = "کاربر دفتر را ترک کرده و پرونده فوری است";
        var forceReleaseResponse = await managerClient.PostAsJsonAsync(
            $"/api/locks/{lockId}/force-release", new ForceReleaseLockRequest(reason));
        Assert.Equal(HttpStatusCode.NoContent, forceReleaseResponse.StatusCode);

        var completed = await Task.WhenAny(revoked.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(revoked.Task, completed);
        var payload = await revoked.Task;
        Assert.Equal("Policy", payload.entityType);
        Assert.Equal(entityId, payload.entityId);
        Assert.Equal(reason, payload.reason);

        await using var verify = TestDbContextFactory.Create();
        AgencyContext.Current = fixture.AgencyAId;
        var audit = await verify.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.EntityId == entityId && a.Action == AuditAction.LockForceReleased);
        Assert.Equal(entityId, audit.PolicyId);
        Assert.Contains(reason, audit.Description);

        var statusResponse = await staffClient.GetAsync($"/api/locks/status?entityType=Policy&entityId={entityId}");
        // [ApiController] turns an Ok(null) result into 204 No Content — no active lock remains.
        Assert.Equal(HttpStatusCode.NoContent, statusResponse.StatusCode);
    }

    private async Task<(DevSeeder.SeededAuthFixture Fixture, HttpClient StaffClient, HttpClient ManagerClient)> SeedAndLoginBothAsync()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);

        var staffClient = await LoginAsync(fixture.AgencyOnlyStaffMobile, fixture.AgencyAId);
        var managerClient = await LoginAsync(fixture.DualAgencyManagerMobile, fixture.AgencyAId);

        return (fixture, staffClient, managerClient);
    }

    private async Task<HttpClient> LoginAsync(string mobile, Guid agencyId)
    {
        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(mobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", agencyId.ToString());
        return client;
    }

    private sealed record LockRevokedPayload(string entityType, Guid entityId, string reason, string by);
}
