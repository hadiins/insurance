using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Aqsat.Api.Contracts;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Aqsat.UnitTests.Release;

/// <summary>
/// Task 23's own check, the HTTP-endpoint half: registering a signed package makes it appear in the
/// panel's catalog, an invalid signature is refused before it ever becomes a row, a duplicate
/// version is refused, and yanking removes it from what GET /packages offers without deleting the
/// row (docs/UPDATE-SYSTEM.md §8: "flip IsYanked, never delete — an UpdateRun that already applied
/// it still needs a real row to point at").
/// </summary>
[Collection("WebApplicationFactory")]
public class PackageRegistrationEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly RSA _signingKey = RSA.Create(2048);
    private readonly WebApplicationFactory<Program> _factory;

    public PackageRegistrationEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Updater:SigningPublicKeyPem"] = _signingKey.ExportSubjectPublicKeyInfoPem(),
            }));
        });
    }

    [Fact]
    public async Task A_validly_signed_package_registers_and_appears_in_the_catalog()
    {
        var client = await AuthenticatedClientAsync();
        var version = $"1.{Random.Shared.Next(1000, 9999)}.{Random.Shared.Next(100_000, 999_999)}";

        var registerResponse = await client.PostAsJsonAsync("/api/platform/updates/register", Sign(new RegisterPackageRequest(
            version, "registry.example.ir/aqsat-api:" + version, "sha256:" + new string('a', 64), "",
            "خرابی جزئی رفع شد", null, false, false)));

        registerResponse.EnsureSuccessStatusCode();

        var packages = await client.GetFromJsonAsync<List<UpdatePackageDto>>("/api/platform/updates/packages");
        Assert.Contains(packages!, p => p.Version == version);
    }

    [Fact]
    public async Task A_package_signed_by_a_different_key_is_rejected_and_never_becomes_a_row()
    {
        var client = await AuthenticatedClientAsync();
        using var attackerKey = RSA.Create(2048);
        var version = $"1.{Random.Shared.Next(1000, 9999)}.{Random.Shared.Next(100_000, 999_999)}";

        var request = new RegisterPackageRequest(
            version, "registry.example.ir/aqsat-api:" + version, "sha256:" + new string('a', 64), "",
            "تلاش برای ثبت بستهٔ جعلی", null, false, false);
        var signedByAttacker = request with { SignatureBase64 = SignWith(attackerKey, request) };

        var response = await client.PostAsJsonAsync("/api/platform/updates/register", signedByAttacker);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var packages = await client.GetFromJsonAsync<List<UpdatePackageDto>>("/api/platform/updates/packages");
        Assert.DoesNotContain(packages!, p => p.Version == version);
    }

    [Fact]
    public async Task Registering_the_same_version_twice_is_rejected()
    {
        var client = await AuthenticatedClientAsync();
        var version = $"1.{Random.Shared.Next(1000, 9999)}.{Random.Shared.Next(100_000, 999_999)}";
        var request = new RegisterPackageRequest(
            version, "registry.example.ir/aqsat-api:" + version, "sha256:" + new string('a', 64), "",
            "نسخهٔ اول", null, false, false);

        var first = await client.PostAsJsonAsync("/api/platform/updates/register", Sign(request));
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync("/api/platform/updates/register", Sign(request));
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Yanking_a_package_removes_it_from_the_active_catalog_without_deleting_it()
    {
        var client = await AuthenticatedClientAsync();
        var version = $"1.{Random.Shared.Next(1000, 9999)}.{Random.Shared.Next(100_000, 999_999)}";
        var registerResponse = await client.PostAsJsonAsync("/api/platform/updates/register", Sign(new RegisterPackageRequest(
            version, "registry.example.ir/aqsat-api:" + version, "sha256:" + new string('a', 64), "",
            "نسخه‌ای که بعداً معیوب شناخته شد", null, false, false)));
        registerResponse.EnsureSuccessStatusCode();
        var package = await registerResponse.Content.ReadFromJsonAsync<UpdatePackageDto>();

        var yankResponse = await client.PostAsync($"/api/platform/updates/packages/{package!.Id}/yank", null);
        yankResponse.EnsureSuccessStatusCode();

        var packages = await client.GetFromJsonAsync<List<UpdatePackageDto>>("/api/platform/updates/packages");
        Assert.DoesNotContain(packages!, p => p.Id == package.Id);
    }

    private RegisterPackageRequest Sign(RegisterPackageRequest unsigned) =>
        unsigned with { SignatureBase64 = SignWith(_signingKey, unsigned) };

    private static string SignWith(RSA key, RegisterPackageRequest request)
    {
        var payload = $"{request.Version}|{request.ImageTag}|{request.Sha256}";
        var signatureBytes = key.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        return Convert.ToBase64String(signatureBytes);
    }

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        await using var seedContext = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(seedContext);

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(fixture.DualAgencyManagerMobile, DevSeeder.SeededUserPassword));
        loginResponse.EnsureSuccessStatusCode();
        var token = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", fixture.AgencyAId.ToString());
        return client;
    }
}
