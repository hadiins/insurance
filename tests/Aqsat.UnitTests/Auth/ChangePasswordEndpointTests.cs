using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aqsat.Api.Contracts;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Aqsat.UnitTests.Auth;

/// <summary>Self-service password change — works for any logged-in user (owner, manager, staff),
/// requires the current password, and the new password actually takes effect on the next login.</summary>
[Collection("WebApplicationFactory")]
public class ChangePasswordEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ChangePasswordEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Wrong_current_password_is_rejected_and_correct_flow_changes_the_login()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var wrongCurrent = await client.PutAsJsonAsync(
            "/api/auth/change-password", new ChangePasswordRequest("not-the-real-password", "NewPass123"));
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);

        var tooShort = await client.PutAsJsonAsync(
            "/api/auth/change-password", new ChangePasswordRequest(DevSeeder.SeededUserPassword, "short"));
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

        var success = await client.PutAsJsonAsync(
            "/api/auth/change-password", new ChangePasswordRequest(DevSeeder.SeededUserPassword, "NewPass123"));
        Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);

        // Old password no longer works; new one does.
        var loginWithOld = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, loginWithOld.StatusCode);

        var loginWithNew = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, "NewPass123"));
        loginWithNew.EnsureSuccessStatusCode();
    }
}
