using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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

    private sealed record BootstrapStatusDto(bool Available);
}
